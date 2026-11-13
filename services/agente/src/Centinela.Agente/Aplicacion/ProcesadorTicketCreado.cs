using Centinela.Agente.Dominio;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// Un TicketCreado de principio a fin, sin pagar dos veces el mismo triaje:
/// <list type="bullet">
/// <item>el ticket se toma en exclusiva: dos réplicas nunca llaman al modelo por el mismo ticket a la vez;</item>
/// <item>un mensaje ya procesado (por id) se descarta;</item>
/// <item>un ticket ya triado (por ticketId) no vuelve al modelo: se republica el mismo evento guardado;</item>
/// <item>con el tope diario de tokens alcanzado, no se llama al modelo: el ticket va a triaje manual.</item>
/// </list>
/// El triaje se guarda antes de publicar. Si el proceso cae en cualquier punto, el mensaje no tiene ack, el broker
/// lo vuelve a entregar y este mismo camino termina el trabajo sin repetir lo hecho.
/// </summary>
public sealed partial class ProcesadorTicketCreado(
    CicloAgente ciclo,
    IAlmacenTriajes almacen,
    IPublicadorTriajes publicador,
    TimeProvider reloj,
    IOptions<OpcionesAgente> opciones,
    ILogger<ProcesadorTicketCreado> log)
{
    public const string ModeloSinLlamada = "sin llamada (tope diario)";

    public async Task<ResultadoProcesamiento> ProcesarAsync(MensajeTicketCreado mensaje, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        // [FLUJO TRIAJE_ASINCRONO 5] Toma el ticket en exclusiva y descarta lo ya procesado → paso 6 (modelo) o 7 (republicar)
        await using var sesion = await almacen.TomarTicketAsync(mensaje.Ticket.TicketId, ct);
        if (sesion is null) return ResultadoProcesamiento.EnCursoEnOtraReplica;
        if (await sesion.MensajeYaProcesadoAsync(mensaje.Id, ct)) return ResultadoProcesamiento.Duplicado;

        var triaje = await sesion.TriajeGuardadoAsync(ct);
        var resultado = ResultadoProcesamiento.Republicado;
        if (triaje is null)
        {
            // [FLUJO TRIAJE_ASINCRONO 6] Revisa el tope diario y tría con el ciclo de la fase 1 (TRIAJE_AGENTE 2–8); guarda antes de publicar → paso 7
            var triado = await TopeDiarioAlcanzadoAsync(ct)
                ? ResultadoTriaje.SinSugerencia(mensaje.Ticket.TicketId, EstadoAnalisis.PresupuestoAgotado,
                    new MetricasTriaje(ModeloSinLlamada, 0, 0, 0, 0))
                : await ciclo.TriarAsync(mensaje.Ticket, ct);
            triaje = TriajeGuardado.De(triado, mensaje.CorrelationId, reloj.GetUtcNow());
            await sesion.GuardarTriajeAsync(triaje, ct);
            resultado = ResultadoProcesamiento.Triado;
        }

        // [FLUJO TRIAJE_ASINCRONO 7] Publica TicketTriado con confirmación del broker y registra el mensaje → ack (consumidor)
        await publicador.PublicarAsync(triaje, ct);
        await sesion.MarcarMensajeProcesadoAsync(mensaje.Id, ct);
        return resultado;
    }

    /// <summary>Tokens gastados desde las 00:00 UTC. Entre réplicas es aproximado: puede pasarse por un triaje.</summary>
    private async Task<bool> TopeDiarioAlcanzadoAsync(CancellationToken ct)
    {
        var tope = opciones.Value.TopeDiarioTokens;
        var ahora = reloj.GetUtcNow();
        var usados = await almacen.TokensUsadosDesdeAsync(new DateTimeOffset(ahora.UtcDateTime.Date, TimeSpan.Zero), ct);
        if (usados < tope) return false;
        TopeDiarioAlcanzado(log, usados, tope);
        return true;
    }

    [LoggerMessage(EventId = 310, Level = LogLevel.Error, Message = "Tope diario de tokens alcanzado ({Usados} de {Tope}): los tickets pasan a triaje manual sin llamar al modelo")]
    private static partial void TopeDiarioAlcanzado(ILogger logger, long usados, long tope);
}
