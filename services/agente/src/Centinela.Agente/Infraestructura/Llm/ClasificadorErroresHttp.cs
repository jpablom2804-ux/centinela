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
        var (tipoApi, mensajeApi) = LeerError(cuerpo);
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
                _ => (TipoErrorLlm.SolicitudInvalida, false),
            };

        return new ErrorLlmException(tipo, reintentable, codigo, espera);
    }

    /// <summary>Lee tipo y mensaje del error. Los dos formatos ponen el detalle dentro de "error".</summary>
    private static (string Tipo, string Mensaje) LeerError(string cuerpo)
    {
        try
        {
            var error = JsonNode.Parse(cuerpo)?["error"];
            var tipo = error?["type"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : string.Empty;
            var mensaje = error?["message"] is JsonValue m && m.TryGetValue<string>(out var x) ? x : string.Empty;
            return (tipo, mensaje);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }
}
