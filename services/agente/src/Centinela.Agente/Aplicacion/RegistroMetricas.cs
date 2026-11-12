using Centinela.Agente.Dominio;
using Microsoft.Extensions.Logging;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// Un solo lugar para el formato de las métricas, porque de él dependen los KPIs de costo.
/// Solo IDs y números: nunca prompts, textos de tickets ni respuestas del modelo.
/// </summary>
public static partial class RegistroMetricas
{
    // [FLUJO TRIAJE_AGENTE 8] Registra estado, modelo, tokens, iteraciones y duración → Program imprime la salida
    public static void Registrar(ILogger logger, ResultadoTriaje resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var m = resultado.Metricas;
        TriajeTerminado(logger, resultado.TicketId, resultado.Estado, m.Modelo, m.TokensEntrada, m.TokensSalida, m.Iteraciones, m.DuracionMs);
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "Triaje terminado {TicketId}: {Estado}, modelo {Modelo}, tokens {TokensEntrada}/{TokensSalida}, {Iteraciones} iteraciones, {DuracionMs} ms")]
    private static partial void TriajeTerminado(ILogger logger, Guid ticketId, EstadoAnalisis estado, string modelo,
        int tokensEntrada, int tokensSalida, int iteraciones, long duracionMs);
}
