using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Puertos;

/// <summary>Publica el TicketTriado. Termina solo cuando el broker lo confirmó y llegó a una cola; si no, lanza.</summary>
public interface IPublicadorTriajes
{
    Task PublicarAsync(TriajeGuardado triaje, CancellationToken ct);
}
