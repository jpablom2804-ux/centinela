using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Llm;

/// <summary>
/// Clasificación de errores HTTP compartida por los dos adaptadores: la regla de qué se reintenta
/// tiene que ser la misma sin importar el proveedor.
/// </summary>
public static class ClasificadorErroresHttp
{
    public static ErrorLlmException Clasificar(HttpResponseMessage respuesta, string cuerpo)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var codigo = (int)respuesta.StatusCode;
        var (tipoApi, codigoApi) = LeerError(cuerpo);
        var espera = respuesta.Headers.RetryAfter?.Delta;

        // A18: saldo agotado solo por código, nunca por palabras del mensaje (un proveedor puede repetir en el
        // error texto generado a partir del ticket). 402 y billing_error (Anthropic, OpenRouter) o
        // insufficient_quota (OpenAI y compatibles, que llega como 429 y no debe reintentarse).
        var sinSaldo = codigo == 402 || tipoApi is "billing_error" or "insufficient_quota" || codigoApi == "insufficient_quota";

        var (tipo, reintentable) = sinSaldo
            ? (TipoErrorLlm.SaldoInsuficiente, false)
            : respuesta.StatusCode switch
            {
                HttpStatusCode.Unauthorized => (TipoErrorLlm.Autenticacion, false),
                HttpStatusCode.Forbidden => (TipoErrorLlm.Permiso, false),
                HttpStatusCode.TooManyRequests => (TipoErrorLlm.LimiteTasa, true),
                HttpStatusCode.RequestTimeout => (TipoErrorLlm.Timeout, true),
                HttpStatusCode.Conflict => (TipoErrorLlm.ErrorServidor, true),
                _ when codigo == 529 => (TipoErrorLlm.Saturado, true),
                _ when codigo >= 500 => (TipoErrorLlm.ErrorServidor, true),
                _ when codigo == 400 && codigoApi == "tool_use_failed" => (TipoErrorLlm.GeneracionInvalida, true),
                _ => (TipoErrorLlm.SolicitudInvalida, false),
            };

        return new ErrorLlmException(tipo, reintentable, codigo, espera, codigoApi: codigoApi);
    }

    /// <summary>
    /// Lee tipo y código del error (los formatos ponen el detalle dentro de "error"). El mensaje no se usa
    /// ni sale hacia afuera: algunos proveedores incluyen en él lo que generó el modelo.
    /// </summary>
    private static (string Tipo, string? Codigo) LeerError(string cuerpo)
    {
        try
        {
            var error = JsonNode.Parse(cuerpo)?["error"];
            var tipo = Texto(error?["type"]);
            var codigo = Texto(error?["code"]) is { Length: > 0 } c ? c : tipo;
            return (tipo, EsCodigoSeguro(codigo) ? codigo : null);
        }
        catch (JsonException)
        {
            return (string.Empty, null);
        }
    }

    /// <summary>
    /// A26: una respuesta 200 que no se puede interpretar (no es JSON, falta un id…) es un error no
    /// reintentable y clasificado, no una excepción suelta que en la fase 3 se reintentaría y se volvería a pagar.
    /// </summary>
    public static RespuestaLlm InterpretarSeguro(Func<string, RespuestaLlm> interpretar, string cuerpo, int codigoHttp)
    {
        ArgumentNullException.ThrowIfNull(interpretar);
        try
        {
            return interpretar(cuerpo);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or InvalidCastException or FormatException or KeyNotFoundException or ArgumentException)
        {
            throw new ErrorLlmException(TipoErrorLlm.ErrorServidor, reintentable: false, codigoHttp, causa: e, codigoApi: "respuesta_invalida");
        }
    }

    private static string Texto(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static bool EsCodigoSeguro(string codigo) =>
        codigo.Length is > 0 and <= 40 && codigo.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}
