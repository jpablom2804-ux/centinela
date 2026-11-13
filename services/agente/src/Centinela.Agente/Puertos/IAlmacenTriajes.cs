using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Puertos;

/// <summary>Persistencia del agente (su propio esquema): mensajes procesados y triajes ya hechos.</summary>
public interface IAlmacenTriajes
{
    /// <summary>
    /// Toma el ticket en exclusiva entre todas las réplicas. Devuelve null si otra réplica lo tiene tomado.
    /// Si la réplica cae, el bloqueo se libera solo.
    /// </summary>
    Task<ISesionTicket?> TomarTicketAsync(Guid ticketId, CancellationToken ct);
}

/// <summary>Operaciones sobre un ticket tomado en exclusiva. Al liberarla, otra réplica puede tomarlo.</summary>
public interface ISesionTicket : IAsyncDisposable
{
    Task<bool> MensajeYaProcesadoAsync(Guid mensajeId, CancellationToken ct);

    Task<TriajeGuardado?> TriajeGuardadoAsync(CancellationToken ct);

    Task GuardarTriajeAsync(TriajeGuardado triaje, CancellationToken ct);

    Task MarcarMensajeProcesadoAsync(Guid mensajeId, CancellationToken ct);
}
