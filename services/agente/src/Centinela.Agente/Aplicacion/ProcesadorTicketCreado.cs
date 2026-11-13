using Centinela.Agente.Puertos;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// Un TicketCreado de principio a fin, sin pagar dos veces el mismo triaje:
/// <list type="bullet">
/// <item>el ticket se toma en exclusiva: dos réplicas nunca llaman al modelo por el mismo ticket a la vez;</item>
/// <item>un mensaje ya procesado (por id) se descarta;</item>
/// <item>un ticket ya triado (por ticketId) no vuelve al modelo: se republica el mismo evento guardado.</item>
/// </list>
/// El triaje se guarda antes de publicar. Si el proceso cae en cualquier punto, el mensaje no tiene ack, el broker
/// lo vuelve a entregar y este mismo camino termina el trabajo sin repetir lo hecho.
/// </summary>
public sealed class ProcesadorTicketCreado(
    CicloAgente ciclo,
    IAlmacenTriajes almacen,
    IPublicadorTriajes publicador,
    TimeProvider reloj)
{
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
            // [FLUJO TRIAJE_ASINCRONO 6] Triaje con el ciclo de la fase 1 (TRIAJE_AGENTE 2–8) y se guarda antes de publicar → paso 7
            var triado = await ciclo.TriarAsync(mensaje.Ticket, ct);
            triaje = TriajeGuardado.De(triado, mensaje.CorrelationId, reloj.GetUtcNow());
            await sesion.GuardarTriajeAsync(triaje, ct);
            resultado = ResultadoProcesamiento.Triado;
        }

        // [FLUJO TRIAJE_ASINCRONO 7] Publica TicketTriado con confirmación del broker y registra el mensaje → ack (consumidor)
        await publicador.PublicarAsync(triaje, ct);
        await sesion.MarcarMensajeProcesadoAsync(mensaje.Id, ct);
        return resultado;
    }
}
