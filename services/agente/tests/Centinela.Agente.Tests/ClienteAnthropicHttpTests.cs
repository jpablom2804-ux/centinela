using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Centinela.Agente.Aplicacion;
using Centinela.Agente.Infraestructura.Llm;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Tests;

public class ClienteAnthropicHttpTests
{
    private sealed class ManejadorFalso(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }
        public JsonObject? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticion = request;
            Cuerpo = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return responder();
        }
    }

    private static readonly OpcionesLlm Opciones = new() { ApiKey = "clave-de-prueba", Modelo = "claude-opus-5-5" };

    private static (ClienteAnthropicHttp Cliente, ManejadorFalso Manejador) Crear(HttpStatusCode codigo, string cuerpo, Action<HttpResponseMessage>? ajustar = null)
    {
        var manejador = new ManejadorFalso(() =>
        {
            var respuesta = new HttpResponseMessage(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
            ajustar?.Invoke(respuesta);
            return respuesta;
        });
        return (new ClienteAnthropicHttp(new HttpClient(manejador), Microsoft.Extensions.Options.Options.Create(Opciones)), manejador);
    }

    private static SolicitudLlm Solicitud(params MensajeConversacion[] mensajes) => new(
        "instrucciones fijas",
        Fabrica.Herramientas().Select(h => h.Definicion).ToList(),
        ValidadorTriaje.Esquema(),
        mensajes);

    private const string RespuestaHerramienta = """
        {"id":"msg_1","model":"claude-opus-5-5","stop_reason":"tool_use",
         "content":[{"type":"thinking","thinking":"","signature":"firma"},
                    {"type":"tool_use","id":"toolu_9","name":"buscar_tickets_similares","input":{"consulta":"vpn"}}],
         "usage":{"input_tokens":800,"cache_read_input_tokens":1200,"cache_creation_input_tokens":0,"output_tokens":60}}
        """;

    [Fact]
    public async Task Arma_la_peticion_con_headers_herramientas_estrictas_y_salida_estructurada()
    {
        var (cliente, manejador) = Crear(HttpStatusCode.OK, RespuestaHerramienta);
        var asistente = new MensajeAsistente(JsonNode.Parse("""[{"type":"thinking","thinking":"","signature":"firma"}]""")!.AsArray());
        var solicitud = Solicitud(
            new MensajeUsuario("hola"),
            asistente,
            new ResultadosHerramientas([new ResultadoHerramienta("toolu_9", "sin resultados", EsError: true)]));

        await cliente.EnviarAsync(solicitud, CancellationToken.None);

        var p = manejador.Peticion!;
        Assert.Equal("https://api.anthropic.com/v1/messages", p.RequestUri!.ToString());
        Assert.Equal("clave-de-prueba", p.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2026-10-21", p.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("server-side-fallback-2026-07-01", p.Headers.GetValues("anthropic-beta").Single());

        var c = manejador.Cuerpo!;
        Assert.Equal("claude-opus-5-5", (string?)c["model"]);
        Assert.Equal("default", (string?)c["fallbacks"]);
        Assert.Equal("auto", (string?)c["tool_choice"]!["type"]);
        Assert.Equal("ephemeral", (string?)c["system"]![0]!["cache_control"]!["type"]);
        Assert.All(c["tools"]!.AsArray(), t => Assert.True((bool)t!["strict"]!));
        Assert.Equal("json_schema", (string?)c["output_config"]!["format"]!["type"]);
        Assert.Equal("medium", (string?)c["output_config"]!["effort"]);

        var mensajes = c["messages"]!.AsArray();
        Assert.Equal("firma", (string?)mensajes[1]!["content"]![0]!["signature"]);
        Assert.True((bool)mensajes[2]!["content"]![0]!["is_error"]!);
        Assert.Equal("toolu_9", (string?)mensajes[2]!["content"]![0]!["tool_use_id"]);
    }

    [Fact]
    public async Task Interpreta_tool_use_suma_tokens_de_cache_y_conserva_el_contenido()
    {
        var (cliente, _) = Crear(HttpStatusCode.OK, RespuestaHerramienta);

        var respuesta = await cliente.EnviarAsync(Solicitud(new MensajeUsuario("hola")), CancellationToken.None);

        Assert.Equal(MotivoParada.UsoHerramientas, respuesta.Motivo);
        var llamada = Assert.Single(respuesta.Llamadas);
        Assert.Equal("toolu_9", llamada.Id);
        Assert.Equal("vpn", llamada.Entrada.GetProperty("consulta").GetString());
        Assert.Equal(new UsoTokens(2_000, 60), respuesta.Uso);
        Assert.Equal(2, ((JsonArray)respuesta.ContenidoProveedor).Count);
    }

    [Fact]
    public async Task Interpreta_un_rechazo_con_su_categoria()
    {
        var (cliente, _) = Crear(HttpStatusCode.OK, """
            {"model":"claude-opus-5-5","stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"},
             "content":[],"usage":{"input_tokens":10,"output_tokens":0}}
            """);

        var respuesta = await cliente.EnviarAsync(Solicitud(new MensajeUsuario("hola")), CancellationToken.None);

        Assert.Equal(MotivoParada.Rechazo, respuesta.Motivo);
        Assert.Equal("cyber", respuesta.DetalleRechazo);
    }

    [Theory]
    [InlineData(402, """{"type":"error","error":{"type":"billing_error","message":"x"}}""", TipoErrorLlm.SaldoInsuficiente, false)]
    [InlineData(400, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}""", TipoErrorLlm.SaldoInsuficiente, false)]
    [InlineData(401, """{"type":"error","error":{"type":"authentication_error","message":"x"}}""", TipoErrorLlm.Autenticacion, false)]
    [InlineData(400, """{"type":"error","error":{"type":"invalid_request_error","message":"bad"}}""", TipoErrorLlm.SolicitudInvalida, false)]
    [InlineData(429, """{"type":"error","error":{"type":"rate_limit_error","message":"x"}}""", TipoErrorLlm.LimiteTasa, true)]
    [InlineData(529, """{"type":"error","error":{"type":"overloaded_error","message":"x"}}""", TipoErrorLlm.Saturado, true)]
    [InlineData(500, "no es json", TipoErrorLlm.ErrorServidor, true)]
    public async Task Clasifica_los_errores_de_la_api(int codigo, string cuerpo, TipoErrorLlm tipo, bool reintentable)
    {
        var (cliente, _) = Crear((HttpStatusCode)codigo, cuerpo, r => r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3)));

        var error = await Assert.ThrowsAsync<ErrorLlmException>(() => cliente.EnviarAsync(Solicitud(new MensajeUsuario("hola")), CancellationToken.None));

        Assert.Equal(tipo, error.Tipo);
        Assert.Equal(reintentable, error.Reintentable);
        Assert.Equal(TimeSpan.FromSeconds(3), error.EsperaSugerida);
        Assert.DoesNotContain("clave-de-prueba", error.ToString(), StringComparison.Ordinal);
    }
}
