namespace Centinela.Agente.Puertos;

/// <summary>
/// Lo que las herramientas pueden consultar, siempre de solo lectura. Dos implementaciones reales: la API interna
/// de mesa-servicio (worker, fase 3) y los datos sintéticos en memoria (modo archivo y pruebas). Todo lo que
/// devuelve es no confiable: lo escribieron personas y pasa por la verificación de enmascarado del ciclo.
/// </summary>
public interface IConsultasMesaServicio
{
    /// <param name="ticketEnCurso">Lo pone el ciclo (nunca el modelo); solo sirve para no devolverlo como similar.</param>
    Task<IReadOnlyList<TicketSimilar>> BuscarTicketsSimilaresAsync(Guid ticketEnCurso, string consulta, int limite, CancellationToken ct);

    Task<IReadOnlyList<ArticuloEncontrado>> BuscarArticulosAsync(string consulta, int limite, CancellationToken ct);

    Task<IReadOnlyList<IncidenteServicio>> IncidentesAbiertosAsync(string sistema, int limite, CancellationToken ct);
}

public sealed record TicketSimilar(string Id, string Titulo, string? Estado, string? Sistema, string? Categoria, string? Solucion, double Similitud);

public sealed record ArticuloEncontrado(string Id, string Titulo, string Extracto, double Similitud);

public sealed record IncidenteServicio(string Id, string Titulo, string Estado, DateTimeOffset Desde);

/// <summary>La fuente no respondió (caída, timeout, credencial). La herramienta devuelve un error y el ciclo sigue.</summary>
public sealed class ConsultaNoDisponibleException(string motivo, Exception? causa = null) : Exception(motivo, causa);
