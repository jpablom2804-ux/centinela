using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Centinela.Agente.Infraestructura.Mensajeria;

/// <summary>Una conexión por proceso, compartida por el consumidor y el publicador; se reconecta sola si se corta.</summary>
public sealed class ConexionRabbit(IOptions<OpcionesMensajeria> opciones) : IAsyncDisposable
{
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private IConnection? _conexion;

    public async Task<IConnection> ObtenerAsync(CancellationToken ct)
    {
        if (_conexion is { IsOpen: true }) return _conexion;
        await _cerrojo.WaitAsync(ct);
        try
        {
            if (_conexion is { IsOpen: true }) return _conexion;
            var o = opciones.Value;
            var fabrica = new ConnectionFactory
            {
                HostName = o.Host,
                Port = o.Puerto,
                VirtualHost = o.VirtualHost,
                UserName = o.Usuario,
                Password = o.Password!,
                ClientProvidedName = "agente",
                AutomaticRecoveryEnabled = true,
            };
            if (_conexion is not null) await _conexion.DisposeAsync();
            _conexion = await fabrica.CreateConnectionAsync(ct);
            return _conexion;
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexion is not null) await _conexion.DisposeAsync();
        _cerrojo.Dispose();
    }
}
