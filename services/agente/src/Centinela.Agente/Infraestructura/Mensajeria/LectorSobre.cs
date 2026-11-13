using System.Text.Json;
using System.Text.RegularExpressions;
using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Infraestructura.Mensajeria;

/// <summary>
/// Lee y valida un TicketCreado v1 (docs/EVENTOS.md) al recibirlo. Lo que no cumple el contrato no se procesa:
/// va a la DLQ. Los errores describen el problema sin copiar el contenido (no debe terminar en logs).
/// </summary>
public static partial class LectorSobre
{
    public const string TipoTicketCreado = "TicketCreado";

    private sealed record Sobre(Guid Id, string? Tipo, int Version, string? CorrelationId, EntradaTicket? Datos);

    public static (MensajeTicketCreado? Mensaje, string? Error) Leer(ReadOnlySpan<byte> cuerpo)
    {
        Sobre? sobre;
        try
        {
            sobre = JsonSerializer.Deserialize<Sobre>(cuerpo, FormatoSalida.Opciones);
        }
        catch (JsonException)
        {
            return (null, "JSON inválido");
        }

        if (sobre is null) return (null, "mensaje vacío");
        if (sobre.Tipo != TipoTicketCreado) return (null, "tipo no esperado");
        if (sobre.Version != 1) return (null, "versión no soportada");
        if (sobre.Id == Guid.Empty) return (null, "id vacío");
        if (sobre.CorrelationId is null || !PatronCorrelacion().IsMatch(sobre.CorrelationId)) return (null, "correlationId inválido");
        if (sobre.Datos is null) return (null, "sin datos");

        var ticket = sobre.Datos.ATicket();
        var errores = ticket.Validar();
        return errores.Count > 0
            ? (null, "ticket inválido: " + string.Join("; ", errores))
            : (new MensajeTicketCreado(sobre.Id, sobre.CorrelationId, ticket), null);
    }

    // El mismo formato que acepta mesa-servicio: X-Request-ID de nginx o un UUID.
    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex PatronCorrelacion();
}
