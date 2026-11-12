namespace Centinela.Agente.Dominio;

public enum EstadoAnalisis
{
    Completado,
    Invalido,
    PresupuestoAgotado,
    Error,
}

/// <summary>Lo que el agente recomienda. Nunca se aplica solo: un técnico la acepta, corrige o descarta.</summary>
public sealed record TriajeSugerido(
    string Categoria,
    string SistemaAfectado,
    string PrioridadSugerida,
    string Justificacion,
    IReadOnlyList<string> DuplicadosPosibles,
    IReadOnlyList<string> ArticulosSugeridos,
    string BorradorRespuesta);

public sealed record MetricasTriaje(string Modelo, int TokensEntrada, int TokensSalida, int Iteraciones, long DuracionMs);

/// <summary>Resultado del triaje. Si el estado no es Completado, no hay sugerencia: el ticket va a triaje manual.</summary>
public sealed record ResultadoTriaje
{
    private ResultadoTriaje(Guid ticketId, EstadoAnalisis estado, TriajeSugerido? sugerencia, MetricasTriaje metricas)
    {
        TicketId = ticketId;
        Estado = estado;
        Sugerencia = sugerencia;
        Metricas = metricas;
    }

    public Guid TicketId { get; }
    public EstadoAnalisis Estado { get; }
    public TriajeSugerido? Sugerencia { get; }
    public MetricasTriaje Metricas { get; }

    public static ResultadoTriaje Completado(Guid ticketId, TriajeSugerido sugerencia, MetricasTriaje metricas) =>
        new(ticketId, EstadoAnalisis.Completado, sugerencia, metricas);

    /// <summary>Cualquier falla deja el ticket sin sugerencia. Nunca se aprueba ni se sugiere por defecto.</summary>
    public static ResultadoTriaje SinSugerencia(Guid ticketId, EstadoAnalisis estado, MetricasTriaje metricas)
    {
        if (estado == EstadoAnalisis.Completado)
        {
            throw new ArgumentException("Un triaje completado necesita una sugerencia.", nameof(estado));
        }

        return new(ticketId, estado, null, metricas);
    }
}

/// <summary>Límites de un triaje: al superar cualquiera, el ticket pasa a triaje manual.</summary>
public sealed record PresupuestoTriaje
{
    public PresupuestoTriaje(int maxIteraciones, int maxTokens, TimeSpan tiempoMaximo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxIteraciones);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tiempoMaximo, TimeSpan.Zero);
        MaxIteraciones = maxIteraciones;
        MaxTokens = maxTokens;
        TiempoMaximo = tiempoMaximo;
    }

    public int MaxIteraciones { get; }
    public int MaxTokens { get; }
    public TimeSpan TiempoMaximo { get; }

    public bool QuedanIteraciones(int usadas) => usadas < MaxIteraciones;

    public bool QuedanTokens(int usados) => usados < MaxTokens;
}
