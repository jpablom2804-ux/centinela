namespace Centinela.Agente.Dominio;

/// <summary>
/// Ticket que llega para triaje. Todo su texto es NO CONFIABLE: lo escribió una persona
/// y puede traer instrucciones disfrazadas. Llega ya enmascarado desde mesa-servicio.
/// </summary>
public sealed record Ticket(
    Guid TicketId,
    string Tipo,
    string Titulo,
    string Descripcion,
    string? SistemaReportado,
    string Canal)
{
    public const int MaxTitulo = 200;
    public const int MaxDescripcion = 5000;
    public const int MaxSistemaReportado = 100;

    public static readonly IReadOnlySet<string> TiposValidos =
        new HashSet<string>(StringComparer.Ordinal) { "incidente", "solicitud", "problema", "cambio" };

    public static readonly IReadOnlySet<string> CanalesValidos =
        new HashSet<string>(StringComparer.Ordinal) { "portal", "correo", "telefono" };

    /// <summary>Devuelve los errores de validación; vacío si el ticket es válido.</summary>
    public IReadOnlyList<string> Validar()
    {
        var errores = new List<string>();
        if (TicketId == Guid.Empty) errores.Add("ticketId vacío");
        if (!TiposValidos.Contains(Tipo)) errores.Add("tipo no válido");
        if (!CanalesValidos.Contains(Canal)) errores.Add("canal no válido");
        if (string.IsNullOrWhiteSpace(Titulo) || Titulo.Length > MaxTitulo) errores.Add($"titulo vacío o de más de {MaxTitulo} caracteres");
        if (string.IsNullOrWhiteSpace(Descripcion) || Descripcion.Length > MaxDescripcion) errores.Add($"descripcion vacía o de más de {MaxDescripcion} caracteres");
        if (SistemaReportado is { Length: > MaxSistemaReportado }) errores.Add($"sistemaReportado de más de {MaxSistemaReportado} caracteres");
        return errores;
    }
}
