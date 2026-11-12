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
        var (tipoApi, mensajeApi, codigoApi) = LeerError(cuerpo);
        var espera = respuesta.Headers.RetryAfter?.Delta;

        // Saldo agotado: 402 (Anthropic billing_error, OpenRouter), o 400 con ese mensaje en algunas cuentas.
        var sinSaldo = codigo == 402 || tipoApi == "billing_error"
            || (codigo == 400 && (mensajeApi.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
                                  || mensajeApi.Contains("insufficient", StringComparison.OrdinalIgnoreCase)));

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
    /// Lee tipo, mensaje y código del error (los formatos ponen el detalle dentro de "error"). El mensaje solo
    /// se usa para clasificar; hacia afuera sale únicamente un código corto, porque algunos proveedores
    /// incluyen en el error lo que generó el modelo, que puede traer texto del ticket.
    /// </summary>
    private static (string Tipo, string Mensaje, string? Codigo) LeerError(string cuerpo)
    {
        try
        {
            var error = JsonNode.Parse(cuerpo)?["error"];
            var tipo = Texto(error?["type"]);
            var mensaje = Texto(error?["message"]);
            var codigo = Texto(error?["code"]) is { Length: > 0 } c ? c : tipo;
            return (tipo, mensaje, EsCodigoSeguro(codigo) ? codigo : null);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty, null);
        }
    }

    private static string Texto(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static bool EsCodigoSeguro(string codigo) =>
        codigo.Length is > 0 and <= 40 && codigo.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}
