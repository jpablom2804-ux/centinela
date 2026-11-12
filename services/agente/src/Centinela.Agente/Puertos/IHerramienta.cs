using System.Text.Json;

namespace Centinela.Agente.Puertos;

/// <summary>
/// Herramienta de solo lectura que el modelo puede pedir. Interfaz porque hay varias
/// implementaciones reales y en la fase 3 pasan a consultar la API interna de mesa-servicio.
/// </summary>
public interface IHerramienta
{
    DefinicionHerramienta Definicion { get; }

    /// <summary>
    /// <paramref name="entrada"/> la eligió el modelo: se valida siempre. <paramref name="contexto"/>
    /// lo inyecta el ciclo; el modelo nunca decide sobre qué ticket trabaja.
    /// </summary>
    Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct);
}

public sealed record ContextoHerramienta(Guid TicketId);

/// <summary>
/// <paramref name="IdsDevueltos"/> son los tickets o artículos que la herramienta mostró: el modelo
/// solo puede sugerir IDs que vio en esta conversación.
/// </summary>
public sealed record ResultadoEjecucion(string Contenido, bool EsError, IReadOnlyCollection<string> IdsDevueltos)
{
    public static ResultadoEjecucion Error(string mensaje) => new(mensaje, EsError: true, []);
}
