using Centinela.Agente.Aplicacion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Centinela.Agente.Infraestructura.Mensajeria;

/// <summary>
/// Worker que consume agente.tickets-creados de a un mensaje (prefetch 1) y con ack manual: el ack va solo después
/// de publicar el TicketTriado. Si el proceso cae a mitad, el broker vuelve a entregar el mensaje.
/// <list type="bullet">
/// <item>Mensaje que no cumple el contrato: a la DLQ, sin reintentar.</item>
/// <item>Falla de infraestructura (base de datos, broker): reintentos con backoff y un máximo; después, a la DLQ.</item>
/// <item>Las fallas del modelo no llegan aquí: el ciclo las convierte en un triaje sin sugerencia (triaje manual).</item>
/// </list>
/// </summary>
public sealed partial class ConsumidorTicketsCreados(
    ConexionRabbit conexion,
    IServiceScopeFactory alcances,
    IOptions<OpcionesMensajeria> opciones,
    ILogger<ConsumidorTicketsCreados> log) : BackgroundService
{
    private static readonly TimeSpan MaxEsperaConexion = TimeSpan.FromSeconds(30);

    // El mismo correlationId que viene de mesa-servicio va en cada log del mensaje (JSON, con IncludeScopes).
    private static readonly Func<ILogger, string, IDisposable?> AlcanceCorrelacion =
        LoggerMessage.DefineScope<string>("correlationId={CorrelationId}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var canal = await AbrirCanalAsync(stoppingToken);
        if (canal is null) return;
        await using (canal)
        {
            await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);
            var consumidor = new AsyncEventingBasicConsumer(canal);
            consumidor.ReceivedAsync += (_, entrega) => AtenderAsync(canal, entrega, stoppingToken);
            await canal.BasicConsumeAsync(opciones.Value.Cola, autoAck: false, consumidor, stoppingToken);
            Escuchando(log, opciones.Value.Cola);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Apagado ordenado: lo que no tuvo ack vuelve a la cola.
            }
        }
    }

    /// <summary>Espera al broker con backoff: el agente puede arrancar antes que RabbitMQ.</summary>
    private async Task<IChannel?> AbrirCanalAsync(CancellationToken ct)
    {
        var espera = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var conectada = await conexion.ObtenerAsync(ct);
                return await conectada.CreateChannelAsync(cancellationToken: ct);
            }
            catch (BrokerUnreachableException)
            {
                BrokerNoDisponible(log, espera.TotalSeconds);
            }

            try
            {
                await Task.Delay(espera, ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            espera = TimeSpan.FromTicks(Math.Min(espera.Ticks * 2, MaxEsperaConexion.Ticks));
        }

        return null;
    }

    // [FLUJO TRIAJE_ASINCRONO 4] Recibe TicketCreado, valida el sobre (contrato v1) y lo procesa; ack solo al terminar → paso 5
    private async Task AtenderAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        var (mensaje, error) = LectorSobre.Leer(entrega.Body.Span);
        if (mensaje is null)
        {
            MensajeInvalido(log, entrega.BasicProperties.MessageId ?? "sin id", error ?? "desconocido");
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        using var _ = AlcanceCorrelacion(log, mensaje.CorrelationId);
        var o = opciones.Value;
        for (var intento = 0; ; intento++)
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                var procesador = alcance.ServiceProvider.GetRequiredService<ProcesadorTicketCreado>();
                var resultado = await procesador.ProcesarAsync(mensaje, ct);
                await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, CancellationToken.None);
                Procesado(log, mensaje.Id, mensaje.Ticket.TicketId, resultado);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Se está apagando: sin ack, el broker lo entrega de nuevo (a esta u otra réplica).
                return;
            }
            catch (Exception e) when (EsFallaDeInfraestructura(e) && intento < o.MaxReintentos)
            {
                var espera = TimeSpan.FromMilliseconds(o.BackoffBaseMs * Math.Pow(2, intento));
                FallaReintentable(log, mensaje.Id, e.GetType().Name, intento + 1, o.MaxReintentos, espera.TotalSeconds);
                await Task.Delay(espera, ct);
            }
            catch (Exception e) when (EsFallaDeInfraestructura(e))
            {
                FallaDefinitiva(log, mensaje.Id, e.GetType().Name, o.MaxReintentos);
                await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
                return;
            }
            catch (Exception e)
            {
                // Un error inesperado no debe dejar el mensaje sin ack bloqueando la cola (prefetch 1). Vuelve a la
                // cola, y el límite de reentregas de la cola quorum (x-delivery-limit) lo manda a la DLQ si se repite.
                ErrorInesperado(log, mensaje.Id, e.GetType().Name);
                await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
                return;
            }
        }
    }

    private static bool EsFallaDeInfraestructura(Exception e) =>
        e is NpgsqlException or PublishException or AlreadyClosedException or OperationInterruptedException
            or BrokerUnreachableException or TimeoutException or OperationCanceledException;

    [LoggerMessage(EventId = 300, Level = LogLevel.Information, Message = "Escuchando la cola {Cola}")]
    private static partial void Escuchando(ILogger logger, string cola);

    [LoggerMessage(EventId = 301, Level = LogLevel.Warning, Message = "RabbitMQ no disponible; reintento en {Segundos} s")]
    private static partial void BrokerNoDisponible(ILogger logger, double segundos);

    [LoggerMessage(EventId = 302, Level = LogLevel.Warning, Message = "Mensaje {MensajeId} descartado a la DLQ: {Error}")]
    private static partial void MensajeInvalido(ILogger logger, string mensajeId, string error);

    [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "Mensaje {MensajeId} del ticket {TicketId}: {Resultado}")]
    private static partial void Procesado(ILogger logger, Guid mensajeId, Guid ticketId, ResultadoProcesamiento resultado);

    [LoggerMessage(EventId = 304, Level = LogLevel.Warning, Message = "Mensaje {MensajeId}: falla {Tipo}; intento {Intento} de {Maximo}, reintento en {Segundos} s")]
    private static partial void FallaReintentable(ILogger logger, Guid mensajeId, string tipo, int intento, int maximo, double segundos);

    [LoggerMessage(EventId = 305, Level = LogLevel.Error, Message = "Mensaje {MensajeId}: falla {Tipo} después de {Maximo} reintentos; va a la DLQ")]
    private static partial void FallaDefinitiva(ILogger logger, Guid mensajeId, string tipo, int maximo);

    [LoggerMessage(EventId = 306, Level = LogLevel.Error, Message = "Mensaje {MensajeId}: error inesperado {Tipo}; vuelve a la cola")]
    private static partial void ErrorInesperado(ILogger logger, Guid mensajeId, string tipo);
}
