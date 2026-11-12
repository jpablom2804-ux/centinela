using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Infraestructura.Llm;

/// <summary>
/// Cliente de la API de mensajes de Anthropic escrito a mano con HttpClient (ADR-002): traduce
/// la conversación al formato de la API, interpreta la respuesta y clasifica los errores.
/// </summary>
public sealed class ClienteAnthropicHttp(HttpClient http, IOptions<OpcionesLlm> opciones) : IClienteLlm
{
    private const string BetaFallback = "server-side-fallback-2026-07-01";
    private readonly OpcionesLlm _opciones = opciones.Value;

    // [FLUJO TRIAJE_AGENTE 5] POST /v1/messages, clasifica errores e interpreta stop_reason → vuelve al ciclo (paso 6 o 7)
    public async Task<RespuestaLlm> EnviarAsync(SolicitudLlm solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, _opciones.Endpoint("v1/messages"))
        {
            Content = JsonContent.Create(ConstruirCuerpo(solicitud)),
        };
        peticion.Headers.Add("x-api-key", _opciones.ApiKey);
        peticion.Headers.Add("anthropic-version", _opciones.VersionApi);
        if (_opciones.UsarFallbackServidor) peticion.Headers.Add("anthropic-beta", BetaFallback);

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
            return ClasificadorErroresHttp.InterpretarSeguro(Interpretar, cuerpo, (int)respuesta.StatusCode);
        }
    }

    public JsonObject ConstruirCuerpo(SolicitudLlm solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var cuerpo = new JsonObject
        {
            ["model"] = _opciones.Modelo,
            ["max_tokens"] = _opciones.MaxTokensRespuesta,
            // Las instrucciones son fijas: se cachean y no se mezclan nunca con datos del ticket.
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = solicitud.Instrucciones,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }),
            ["tools"] = new JsonArray(solicitud.Herramientas.Select(h => (JsonNode)new JsonObject
            {
                ["name"] = h.Nombre,
                ["description"] = h.Descripcion,
                ["input_schema"] = h.EsquemaEntrada.DeepClone(),
                ["strict"] = true,
            }).ToArray()),
            ["tool_choice"] = new JsonObject { ["type"] = "auto" },
            ["output_config"] = new JsonObject
            {
                ["effort"] = _opciones.Esfuerzo,
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = solicitud.EsquemaSalida.DeepClone() },
            },
            ["messages"] = new JsonArray(solicitud.Mensajes.Select(Mensaje).ToArray()),
        };
        if (_opciones.UsarFallbackServidor) cuerpo["fallbacks"] = "default";
        return cuerpo;
    }

    private static JsonNode Mensaje(MensajeConversacion mensaje) => mensaje switch
    {
        MensajeUsuario u => new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = u.Texto }),
        },
        // Se reenvía exactamente lo que devolvió la API, incluidos los bloques de razonamiento.
        MensajeAsistente a => new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = ((JsonArray)a.ContenidoProveedor).DeepClone(),
        },
        ResultadosHerramientas r => new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(r.Resultados.Select(x => (JsonNode)new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = x.IdLlamada,
                ["content"] = x.Contenido,
                ["is_error"] = x.EsError,
            }).ToArray()),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(mensaje), mensaje.GetType().Name, "Tipo de mensaje no soportado."),
    };

    public static RespuestaLlm Interpretar(string cuerpo)
    {
        var raiz = JsonNode.Parse(cuerpo)?.AsObject() ?? throw new ErrorLlmException(TipoErrorLlm.ErrorServidor, reintentable: false);
        var contenido = raiz["content"]?.AsArray() ?? [];
        var texto = string.Concat(contenido
            .Where(b => (string?)b?["type"] == "text")
            .Select(b => (string?)b!["text"] ?? string.Empty));

        var llamadas = contenido
            .Where(b => (string?)b?["type"] == "tool_use")
            .Select(b => new LlamadaHerramienta(
                (string?)b!["id"] is { Length: > 0 } id ? id : throw new InvalidOperationException("Llamada a herramienta sin id."),
                (string)b["name"]!,
                JsonSerializer.SerializeToElement(b["input"])))
            .ToList();

        var uso = raiz["usage"];
        var entrada = Entero(uso, "input_tokens") + Entero(uso, "cache_creation_input_tokens") + Entero(uso, "cache_read_input_tokens");
        var motivo = (string?)raiz["stop_reason"] switch
        {
            "end_turn" => MotivoParada.Final,
            "tool_use" => MotivoParada.UsoHerramientas,
            "max_tokens" => MotivoParada.LimiteTokens,
            "refusal" => MotivoParada.Rechazo,
            _ => MotivoParada.Otro,
        };

        return new RespuestaLlm(
            motivo,
            texto,
            llamadas,
            new UsoTokens(entrada, Entero(uso, "output_tokens")),
            (string?)raiz["model"] ?? "desconocido",
            contenido.DeepClone(),
            (string?)raiz["stop_details"]?["category"]);
    }

    private static int Entero(JsonNode? nodo, string nombre) => nodo?[nombre] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
}
