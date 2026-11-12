using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Infraestructura.Llm;

/// <summary>
/// Cliente para APIs compatibles con OpenAI Chat Completions (ADR-006): Qwen en Alibaba Model Studio
/// u OpenRouter, Ollama local y otros. Escrito a mano igual que el de Anthropic; el ciclo no cambia.
/// </summary>
public sealed partial class ClienteOpenAiCompatibleHttp(HttpClient http, IOptions<OpcionesLlm> opciones) : IClienteLlm
{
    private readonly OpcionesLlm _opciones = opciones.Value;

    // [FLUJO TRIAJE_AGENTE 5] POST chat/completions, clasifica errores e interpreta finish_reason → vuelve al ciclo (paso 6 o 7)
    public async Task<RespuestaLlm> EnviarAsync(SolicitudLlm solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(_opciones.UrlBase, "chat/completions"))
        {
            Content = JsonContent.Create(ConstruirCuerpo(solicitud)),
        };
        if (!string.IsNullOrWhiteSpace(_opciones.ApiKey))
        {
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opciones.ApiKey);
        }

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(peticion, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new ErrorLlmException(TipoErrorLlm.Red, reintentable: true, causa: e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new ErrorLlmException(TipoErrorLlm.Timeout, reintentable: true, causa: e);
        }

        using (respuesta)
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode) throw ClasificadorErroresHttp.Clasificar(respuesta, cuerpo);
            return Interpretar(cuerpo);
        }
    }

    public JsonObject ConstruirCuerpo(SolicitudLlm solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        // Sin salida estructurada, el esquema va en las instrucciones; la validación propia no cambia.
        var instrucciones = _opciones.UsarSalidaEstructurada
            ? solicitud.Instrucciones
            : $"{solicitud.Instrucciones}\n\n## Formato de la respuesta final\n\nResponde solo con un objeto JSON, sin texto antes ni después, que cumpla este esquema:\n{solicitud.EsquemaSalida.ToJsonString()}";

        var mensajes = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = instrucciones });
        foreach (var mensaje in solicitud.Mensajes)
        {
            foreach (var nodo in Mensajes(mensaje)) mensajes.Add(nodo);
        }

        var cuerpo = new JsonObject
        {
            ["model"] = _opciones.Modelo,
            ["max_tokens"] = _opciones.MaxTokensRespuesta,
            ["messages"] = mensajes,
            ["tools"] = new JsonArray(solicitud.Herramientas.Select(h => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = h.Nombre,
                    ["description"] = h.Descripcion,
                    ["parameters"] = h.EsquemaEntrada.DeepClone(),
                },
            }).ToArray()),
            ["tool_choice"] = "auto",
        };

        if (_opciones.UsarSalidaEstructurada)
        {
            cuerpo["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "triaje", ["strict"] = true, ["schema"] = solicitud.EsquemaSalida.DeepClone() },
            };
        }

        return cuerpo;
    }

    private static IEnumerable<JsonNode> Mensajes(MensajeConversacion mensaje) => mensaje switch
    {
        MensajeUsuario u => [new JsonObject { ["role"] = "user", ["content"] = u.Texto }],
        // Solo lo que el formato acepta de vuelta: rol, texto y llamadas a herramientas.
        MensajeAsistente a => [Reenviable((JsonObject)a.ContenidoProveedor)],
        // El formato no tiene is_error: el error se marca en el texto del resultado.
        ResultadosHerramientas r => r.Resultados.Select(x => (JsonNode)new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = x.IdLlamada,
            ["content"] = x.EsError ? $"ERROR: {x.Contenido}" : x.Contenido,
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(mensaje), mensaje.GetType().Name, "Tipo de mensaje no soportado."),
    };

    private static JsonObject Reenviable(JsonObject original)
    {
        var mensaje = new JsonObject { ["role"] = "assistant", ["content"] = original["content"]?.DeepClone() };
        if (original["tool_calls"] is JsonArray llamadas) mensaje["tool_calls"] = llamadas.DeepClone();
        return mensaje;
    }

    public static RespuestaLlm Interpretar(string cuerpo)
    {
        var raiz = JsonNode.Parse(cuerpo)?.AsObject() ?? throw new ErrorLlmException(TipoErrorLlm.ErrorServidor, reintentable: false);
        var opcion = raiz["choices"]?.AsArray().FirstOrDefault()?.AsObject()
            ?? throw new ErrorLlmException(TipoErrorLlm.ErrorServidor, reintentable: false);
        var mensaje = opcion["message"]?.AsObject() ?? new JsonObject();

        var llamadas = (mensaje["tool_calls"] as JsonArray ?? [])
            .Select(c => new LlamadaHerramienta(
                (string?)c?["id"] ?? string.Empty,
                (string?)c?["function"]?["name"] ?? string.Empty,
                Argumentos((string?)c?["function"]?["arguments"])))
            .ToList();

        // Algunos servidores (Ollama) devuelven finish_reason "stop" aunque pidan herramientas: mandan las llamadas.
        var motivo = llamadas.Count > 0
            ? MotivoParada.UsoHerramientas
            : (string?)opcion["finish_reason"] switch
            {
                "stop" => MotivoParada.Final,
                "length" => MotivoParada.LimiteTokens,
                "content_filter" => MotivoParada.Rechazo,
                _ => MotivoParada.Otro,
            };

        var uso = raiz["usage"];
        return new RespuestaLlm(
            motivo,
            LimpiarTexto((string?)mensaje["content"] ?? string.Empty),
            llamadas,
            new UsoTokens(Entero(uso, "prompt_tokens"), Entero(uso, "completion_tokens")),
            (string?)raiz["model"] ?? "desconocido",
            mensaje.DeepClone(),
            motivo == MotivoParada.Rechazo ? "content_filter" : null);
    }

    /// <summary>Los argumentos llegan como texto JSON. Si no son JSON válido, la herramienta los rechaza.</summary>
    private static JsonElement Argumentos(string? texto)
    {
        try
        {
            using var documento = JsonDocument.Parse(string.IsNullOrWhiteSpace(texto) ? "{}" : texto);
            return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(texto);
        }
    }

    /// <summary>
    /// Quita lo que algunos modelos agregan alrededor del JSON: el razonamiento de Qwen entre
    /// &lt;think&gt; y &lt;/think&gt;, y los bloques ```json de Markdown.
    /// </summary>
    public static string LimpiarTexto(string texto)
    {
        var limpio = PatronPensamiento().Replace(texto, string.Empty).Trim();
        var bloque = PatronBloqueCodigo().Match(limpio);
        return bloque.Success ? bloque.Groups["json"].Value.Trim() : limpio;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex PatronPensamiento();

    [GeneratedRegex(@"^```(?:json)?\s*(?<json>.*?)\s*```$", RegexOptions.Singleline)]
    private static partial Regex PatronBloqueCodigo();

    private static int Entero(JsonNode? nodo, string nombre) => nodo?[nombre] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
}
