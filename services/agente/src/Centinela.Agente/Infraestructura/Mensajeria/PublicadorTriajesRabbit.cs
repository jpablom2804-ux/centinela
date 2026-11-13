using System.Text;
using Centinela.Agente.Aplicacion;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Centinela.Agente.Infraestructura.Mensajeria;

/// <summary>
/// Publica con confirmaciones del broker y mandatory: si RabbitMQ no lo acepta o no hay cola enlazada,
/// BasicPublishAsync lanza y el mensaje de entrada no recibe ack.
/// </summary>
public sealed class PublicadorTriajesRabbit(ConexionRabbit conexion, IOptions<OpcionesMensajeria> opciones) : IPublicadorTriajes, IAsyncDisposable
{
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private IChannel? _canal;

    public async Task PublicarAsync(TriajeGuardado triaje, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(triaje);
        var o = opciones.Value;
        var propiedades = new BasicProperties
        {
            MessageId = triaje.EventoId.ToString(),
            Type = TriajeGuardado.Tipo,
            CorrelationId = triaje.CorrelationId,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = DeliveryModes.Persistent,
        };
        var cuerpo = Encoding.UTF8.GetBytes(triaje.Cuerpo);

        await _cerrojo.WaitAsync(ct);
        try
        {
            var canal = await CanalAsync(ct);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(o.EsperaConfirmacionSegundos));
            await canal.BasicPublishAsync(o.ExchangeTriajes, o.RutaTriajes, mandatory: true, propiedades, cuerpo, limite.Token);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    private async Task<IChannel> CanalAsync(CancellationToken ct)
    {
        if (_canal is { IsOpen: true }) return _canal;
        if (_canal is not null) await _canal.DisposeAsync();
        var conectada = await conexion.ObtenerAsync(ct);
        _canal = await conectada.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        return _canal;
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null) await _canal.DisposeAsync();
        _cerrojo.Dispose();
    }
}
