using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Aplicacion;
using Centinela.Agente.Dominio;
using Centinela.Agente.Infraestructura.Llm;
using Centinela.Agente.Puertos;
using Centinela.Agente.Tests;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Adversarial;

/// <summary>Casos que salieron del modelado de amenazas del 2026-11-12 (docs/SEGURIDAD.md, A1–A28) para el agente.</summary>
public class CasosModeladoAmenazas
{
    private static JsonElement Caso(string archivo)
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "casos", archivo)));
        return documento.RootElement.Clone();
    }

    private static Ticket Ticket(JsonElement caso) =>
        caso.GetProperty("ticket").Deserialize<EntradaTicket>(FormatoSalida.Opciones)!.ATicket();

    private static CicloAgente Ciclo(IClienteLlm cliente, IEnumerable<IHerramienta> herramientas, LoggerCapturador<CicloAgente>? logger = null) =>
        new(cliente, herramientas, Options.Create(new OpcionesAgente { BackoffBaseMs = 1 }), TimeProvider.System, logger ?? new LoggerCapturador<CicloAgente>());

    [Fact]
    public async Task Caso19_formatos_evasivos_no_llegan_al_modelo()
    {
        var caso = Caso("caso-19-evasion-enmascarado.json");
        var cliente = new ClienteLlmFalso().RespondeSiempre(_ => Respuestas.Final(Respuestas.TriajeVpn()));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Ticket(caso), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Error, resultado.Estado);
        Assert.Empty(cliente.Solicitudes);
    }

    [Fact]
    public async Task Caso30_borradores_con_enlaces_o_invisibles_quedan_invalidos()
    {
        var caso = Caso("caso-30-ticket-envenenado.json");
        foreach (var borrador in caso.GetProperty("borradoresMaliciosos").EnumerateArray().Select(b => b.GetString()!))
        {
            var datos = JsonSerializer.Deserialize<Dictionary<string, object>>(Respuestas.TriajeVpn())!;
            datos["borradorRespuesta"] = borrador;
            var malicioso = JsonSerializer.Serialize(datos);
            var cliente = new ClienteLlmFalso().RespondeSiempre(_ => Respuestas.Final(malicioso));

            var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Ticket(caso), CancellationToken.None);

            Assert.Equal(EstadoAnalisis.Invalido, resultado.Estado);
            Assert.Null(resultado.Sugerencia);
        }
    }

    [Fact]
    public async Task Caso31_cientos_de_llamadas_en_una_respuesta_no_se_ejecutan()
    {
        var caso = Caso("caso-31-cientos-de-herramientas.json");
        var cantidad = caso.GetProperty("llamadasPedidas").GetInt32();
        using var entrada = JsonDocument.Parse("""{"consulta":"revision completa"}""");
        var llamadas = Enumerable.Range(0, cantidad)
            .Select(i => new LlamadaHerramienta($"toolu_{i}", "buscar_tickets_similares", entrada.RootElement.Clone()))
            .ToList();
        var cliente = new ClienteLlmFalso().Responde(new RespuestaLlm(MotivoParada.UsoHerramientas, string.Empty, llamadas,
            new UsoTokens(1_000, 50), "modelo-falso", new JsonArray()));
        var herramienta = new HerramientaContadora();

        var resultado = await Ciclo(cliente, [herramienta]).TriarAsync(Ticket(caso), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.PresupuestoAgotado, resultado.Estado);
        Assert.Equal(0, herramienta.Ejecuciones);
        Assert.Single(cliente.Solicitudes);
    }

    [Fact]
    public async Task Caso32_un_falso_positivo_en_un_resultado_se_omite_y_el_triaje_sigue()
    {
        var caso = Caso("caso-32-falso-positivo-en-resultado.json");
        var herramienta = new HerramientaContadora(caso.GetProperty("contenidoDeHerramienta").GetString()!);
        var cliente = new ClienteLlmFalso()
            .Responde(Respuestas.Herramienta("buscar_tickets_similares", """{"consulta":"windows update"}"""))
            .Responde(Respuestas.Final(Respuestas.TriajeVpn()));

        var resultado = await Ciclo(cliente, [herramienta]).TriarAsync(Ticket(caso), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Completado, resultado.Estado);
        var enviado = Assert.IsType<ResultadosHerramientas>(cliente.Solicitudes[1].Mensajes[2]).Resultados[0];
        Assert.True(enviado.EsError);
        Assert.DoesNotContain("80070005", enviado.Contenido, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caso33_el_saldo_se_clasifica_por_codigo_y_no_por_mensaje()
    {
        var caso = Caso("caso-33-errores-de-saldo.json");
        foreach (var r in caso.GetProperty("respuestas").EnumerateArray())
        {
            var codigo = (HttpStatusCode)r.GetProperty("codigo").GetInt32();
            var cuerpo = r.GetProperty("cuerpo").GetString()!;
            var opciones = new OpcionesLlm
            {
                Proveedor = OpcionesLlm.ProveedorCompatibleOpenAi,
                UrlBase = new Uri("https://proveedor.ejemplo.com/v1/"),
                ApiKey = "k",
                Modelo = "m",
            };
            var cliente = new ClienteOpenAiCompatibleHttp(new HttpClient(new ManejadorFijo(codigo, cuerpo)), Options.Create(opciones));

            var error = await Assert.ThrowsAsync<ErrorLlmException>(() => cliente.EnviarAsync(
                new SolicitudLlm("i", [], ValidadorTriaje.Esquema(), [new MensajeUsuario("hola")]), CancellationToken.None));

            Assert.Equal(Enum.Parse<TipoErrorLlm>(r.GetProperty("tipoEsperado").GetString()!), error.Tipo);
            Assert.Equal(r.GetProperty("reintentable").GetBoolean(), error.Reintentable);
        }
    }

    [Fact]
    public async Task Caso38_un_nombre_inventado_nunca_llega_a_los_logs()
    {
        var caso = Caso("caso-38-nombre-inventado.json");
        var inventado = caso.GetProperty("nombreInventado").GetString()!;
        var logger = new LoggerCapturador<CicloAgente>();
        var cliente = new ClienteLlmFalso()
            .Responde(Respuestas.Herramienta(inventado, "{}"))
            .Responde(Respuestas.Final(Respuestas.TriajeVpn()));

        var resultado = await Fabrica.Ciclo(cliente, logger: logger).TriarAsync(Ticket(caso), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Completado, resultado.Estado);
        Assert.Contains(logger.Registros, r => r.Mensaje.Contains("desconocida", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Registros, r => r.Mensaje.Contains(inventado, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new[] { "KB-002" }, new string[0])]
    [InlineData(new string[0], new[] { "TK-1001" })]
    public async Task Caso39_tickets_y_articulos_no_se_cruzan(string[] duplicados, string[] articulos)
    {
        var caso = Caso("caso-39-ids-cruzados.json");
        var cliente = new ClienteLlmFalso()
            .Responde(Respuestas.Herramienta("buscar_tickets_similares", """{"consulta":"vpn no conecta desde casa"}"""))
            .Responde(Respuestas.Herramienta("buscar_articulos_conocimiento", """{"consulta":"diagnostico vpn corporativa"}""", id: "toolu_2"))
            .RespondeSiempre(_ => Respuestas.Final(Respuestas.TriajeVpn(duplicados: duplicados, articulos: articulos)));

        var resultado = await Fabrica.Ciclo(cliente).TriarAsync(Ticket(caso), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Invalido, resultado.Estado);
    }

    /// <summary>Herramienta de prueba: cuenta ejecuciones y devuelve un contenido fijo.</summary>
    private sealed class HerramientaContadora(string contenido = "sin resultados") : IHerramienta
    {
        public int Ejecuciones { get; private set; }

        public DefinicionHerramienta Definicion { get; } = new("buscar_tickets_similares", "prueba", new JsonObject());

        public Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
        {
            Ejecuciones++;
            var json = JsonSerializer.Serialize(new { resultados = new[] { new { id = "TK-7000", solucion = contenido } } });
            return Task.FromResult(new ResultadoEjecucion(json, EsError: false, ["TK-7000"], TipoIdDevuelto.Ticket));
        }
    }

    private sealed class ManejadorFijo(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
    }
}
