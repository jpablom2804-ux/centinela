using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Centinela.Agente.Aplicacion;
using Centinela.Agente.Dominio;
using Centinela.Agente.Infraestructura.Llm;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Tests;

public class ClienteOpenAiCompatibleHttpTests
{
    /// <summary>Devuelve las respuestas en orden y guarda cada petición.</summary>
    private sealed class ManejadorEnSecuencia(params (HttpStatusCode Codigo, string Cuerpo)[] respuestas) : HttpMessageHandler
    {
        private int _siguiente;

        public List<(HttpRequestMessage Peticion, JsonObject Cuerpo)> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticiones.Add((request, JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject()));
            var (codigo, cuerpo) = respuestas[_siguiente++];
            return new HttpResponseMessage(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        }
    }

    private static OpcionesLlm Qwen(string apiKey = "clave-de-prueba", bool salidaEstructurada = false) => new()
    {
        Proveedor = OpcionesLlm.ProveedorCompatibleOpenAi,
        UrlBase = new Uri("https://proveedor.ejemplo.com/v1/"),
        ApiKey = apiKey,
        Modelo = "qwen-de-prueba",
        UsarSalidaEstructurada = salidaEstructurada,
    };

    private static ClienteOpenAiCompatibleHttp Cliente(ManejadorEnSecuencia manejador, OpcionesLlm? opciones = null) =>
        new(new HttpClient(manejador), Microsoft.Extensions.Options.Options.Create(opciones ?? Qwen()));

    private static SolicitudLlm Solicitud(params MensajeConversacion[] mensajes) =>
        new("instrucciones fijas", Fabrica.Herramientas().Select(h => h.Definicion).ToList(), ValidadorTriaje.Esquema(), mensajes);

    private static string RespuestaConHerramienta(string finishReason = "tool_calls", string argumentos = """{\"consulta\":\"vpn\"}""") => $$$"""
        {"model":"qwen-de-prueba","choices":[{"finish_reason":"{{{finishReason}}}","message":{"role":"assistant","content":null,
          "reasoning":"pensamiento interno",
          "tool_calls":[{"id":"call_1","type":"function","function":{"name":"buscar_tickets_similares","arguments":"{{{argumentos}}}"}}]}}],
         "usage":{"prompt_tokens":900,"completion_tokens":40}}
        """;

    private static string RespuestaFinal(string contenido) =>
        new JsonObject
        {
            ["model"] = "qwen-de-prueba",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = "stop",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = contenido },
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 1200, ["completion_tokens"] = 250 },
        }.ToJsonString();

    [Fact]
    public async Task Arma_la_peticion_en_formato_chat_completions()
    {
        var manejador = new ManejadorEnSecuencia((HttpStatusCode.OK, RespuestaConHerramienta()));
        var asistente = JsonNode.Parse("""{"role":"assistant","content":null,"reasoning":"x","tool_calls":[{"id":"call_1","type":"function","function":{"name":"buscar_tickets_similares","arguments":"{}"}}]}""")!.AsObject();

        await Cliente(manejador).EnviarAsync(Solicitud(
            new MensajeUsuario("hola"),
            new MensajeAsistente(asistente),
            new ResultadosHerramientas([new ResultadoHerramienta("call_1", "sin resultados", EsError: true)])), CancellationToken.None);

        var (peticion, cuerpo) = Assert.Single(manejador.Peticiones);
        Assert.Equal("https://proveedor.ejemplo.com/v1/chat/completions", peticion.RequestUri!.ToString());
        Assert.Equal("Bearer", peticion.Headers.Authorization!.Scheme);
        Assert.Equal("qwen-de-prueba", (string?)cuerpo["model"]);
        Assert.Null(cuerpo["tool_choice"]);
        Assert.Null(cuerpo["response_format"]);
        Assert.All(cuerpo["tools"]!.AsArray(), t => Assert.Equal("function", (string?)t!["type"]));

        var mensajes = cuerpo["messages"]!.AsArray();
        Assert.Equal("system", (string?)mensajes[0]!["role"]);
        Assert.Contains("prioridadSugerida", (string?)mensajes[0]!["content"], StringComparison.Ordinal);
        Assert.Null(mensajes[2]!["reasoning"]);
        Assert.Equal("call_1", (string?)mensajes[2]!["tool_calls"]![0]!["id"]);
        Assert.Equal("tool", (string?)mensajes[3]!["role"]);
        Assert.StartsWith("ERROR:", (string?)mensajes[3]!["content"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Con_salida_estructurada_manda_response_format()
    {
        var manejador = new ManejadorEnSecuencia((HttpStatusCode.OK, RespuestaFinal("{}")));

        await Cliente(manejador, Qwen(salidaEstructurada: true)).EnviarAsync(Solicitud(new MensajeUsuario("hola")), CancellationToken.None);

        var cuerpo = manejador.Peticiones[0].Cuerpo;
        Assert.Equal("json_schema", (string?)cuerpo["response_format"]!["type"]);
        Assert.Equal("instrucciones fijas", (string?)cuerpo["messages"]![0]!["content"]);
    }

    [Fact]
    public async Task Sin_api_key_no_manda_authorization()
    {
        var manejador = new ManejadorEnSecuencia((HttpStatusCode.OK, RespuestaFinal("{}")));
        var local = Qwen(apiKey: string.Empty);
        local.UrlBase = new Uri("http://localhost:11434/v1/");

        await Cliente(manejador, local).EnviarAsync(Solicitud(new MensajeUsuario("hola")), CancellationToken.None);

        Assert.Null(manejador.Peticiones[0].Peticion.Headers.Authorization);
    }

    [Theory]
    [InlineData("tool_calls")]
    [InlineData("stop")]
    public void Las_llamadas_a_herramientas_mandan_aunque_finish_reason_diga_stop(string finishReason)
    {
        var respuesta = ClienteOpenAiCompatibleHttp.Interpretar(RespuestaConHerramienta(finishReason));

        Assert.Equal(MotivoParada.UsoHerramientas, respuesta.Motivo);
        Assert.Equal("vpn", Assert.Single(respuesta.Llamadas).Entrada.GetProperty("consulta").GetString());
        Assert.Equal(new UsoTokens(900, 40), respuesta.Uso);
    }

    [Fact]
    public void Argumentos_que_no_son_json_llegan_como_texto_para_que_la_herramienta_los_rechace()
    {
        var respuesta = ClienteOpenAiCompatibleHttp.Interpretar(RespuestaConHerramienta(argumentos: "consulta=vpn"));

        Assert.Equal(System.Text.Json.JsonValueKind.String, respuesta.Llamadas[0].Entrada.ValueKind);
    }

    [Theory]
    [InlineData("<think>razono un rato</think>\n{\"a\":1}", "{\"a\":1}")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("<think>x</think>```\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("Aquí está el triaje: {\"a\":1} Espero que sirva.", "{\"a\":1}")]
    [InlineData("no hay json aquí", "no hay json aquí")]
    public void Limpia_razonamiento_y_bloques_de_markdown(string contenido, string esperado)
    {
        Assert.Equal(esperado, ClienteOpenAiCompatibleHttp.LimpiarTexto(contenido));
    }

    [Theory]
    [InlineData(402, """{"error":{"message":"Insufficient credits","code":402}}""", TipoErrorLlm.SaldoInsuficiente, false)]
    [InlineData(401, """{"error":{"message":"Invalid API key"}}""", TipoErrorLlm.Autenticacion, false)]
    [InlineData(429, """{"error":{"message":"Rate limit exceeded: free-models-per-day"}}""", TipoErrorLlm.LimiteTasa, true)]
    [InlineData(503, "no es json", TipoErrorLlm.ErrorServidor, true)]
    [InlineData(400, """{"error":{"message":"Failed to call a function","type":"invalid_request_error","code":"tool_use_failed","failed_generation":"texto del modelo"}}""", TipoErrorLlm.GeneracionInvalida, true)]
    public async Task Clasifica_los_errores_igual_que_el_adaptador_de_anthropic(int codigo, string cuerpo, TipoErrorLlm tipo, bool reintentable)
    {
        var manejador = new ManejadorEnSecuencia(((HttpStatusCode)codigo, cuerpo));

        var error = await Assert.ThrowsAsync<ErrorLlmException>(() => Cliente(manejador).EnviarAsync(Solicitud(new MensajeUsuario("hola")), CancellationToken.None));

        Assert.Equal(tipo, error.Tipo);
        Assert.Equal(reintentable, error.Reintentable);
        Assert.DoesNotContain("clave-de-prueba", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_mismo_ciclo_completa_un_triaje_con_este_proveedor()
    {
        var final = "<think>parece la VPN</think>```json\n" + Respuestas.TriajeVpn(duplicados: ["TK-1001"]) + "\n```";
        var manejador = new ManejadorEnSecuencia((HttpStatusCode.OK, RespuestaConHerramienta()), (HttpStatusCode.OK, RespuestaFinal(final)));

        var resultado = await Fabrica.Ciclo(Cliente(manejador)).TriarAsync(Fabrica.TicketVpn(), CancellationToken.None);

        Assert.Equal(EstadoAnalisis.Completado, resultado.Estado);
        Assert.Equal(["TK-1001"], resultado.Sugerencia!.DuplicadosPosibles);
        Assert.Equal("qwen-de-prueba", resultado.Metricas.Modelo);
        Assert.Equal(2_100, resultado.Metricas.TokensEntrada);
    }

    [Theory]
    [InlineData("anthropic", "https://api.anthropic.com/", "", "Falta Llm:ApiKey")]
    [InlineData("openai_compatible", "https://proveedor.ejemplo.com/v1/", "", "Falta Llm:ApiKey")]
    [InlineData("openai_compatible", "http://proveedor.ejemplo.com/v1/", "k", "https")]
    [InlineData("otro", "https://api.anthropic.com/", "k", "Llm:Proveedor")]
    public void Valida_la_coherencia_de_la_configuracion(string proveedor, string url, string apiKey, string errorEsperado)
    {
        var opciones = new OpcionesLlm { Proveedor = proveedor, UrlBase = new Uri(url), ApiKey = apiKey, Modelo = "m" };

        Assert.Contains(opciones.ErroresDeCoherencia(), e => e.Contains(errorEsperado, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://ollama.com/v1")]
    [InlineData("https://ollama.com/v1/")]
    public void La_url_base_funciona_con_o_sin_barra_final(string url)
    {
        var opciones = new OpcionesLlm { UrlBase = new Uri(url) };

        Assert.Equal("https://ollama.com/v1/chat/completions", opciones.Endpoint("chat/completions").ToString());
    }

    [Fact]
    public void Ollama_local_no_necesita_api_key_ni_https()
    {
        var opciones = new OpcionesLlm { Proveedor = "openai_compatible", UrlBase = new Uri("http://localhost:11434/v1/"), Modelo = "qwen3:4b" };

        Assert.Empty(opciones.ErroresDeCoherencia());
    }
}
