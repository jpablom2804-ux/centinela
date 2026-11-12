using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Centinela.Agente.Infraestructura.Herramientas;

public sealed record TicketResuelto(string Id, string Titulo, string Descripcion, string Categoria, string Sistema, string Solucion);

public sealed record Articulo(string Id, string Titulo, string Contenido, IReadOnlyList<string> Sistemas);

public sealed record IncidenteAbierto(string Id, string Sistema, string Titulo, string Estado, DateTimeOffset Desde);

public sealed record DatosPrueba(
    IReadOnlyList<TicketResuelto> TicketsResueltos,
    IReadOnlyList<Articulo> Articulos,
    IReadOnlyList<IncidenteAbierto> IncidentesAbiertos);

/// <summary>
/// Datos 100 % sintéticos en memoria para la fase 1. En la fase 3 las herramientas consultan
/// la API interna de solo lectura de mesa-servicio y este repositorio desaparece.
/// </summary>
public sealed class RepositorioDatosPrueba(DatosPrueba datos)
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public DatosPrueba Datos { get; } = datos;

    public static RepositorioDatosPrueba Cargar(string ruta)
    {
        var json = File.ReadAllText(ruta);
        var datos = JsonSerializer.Deserialize<DatosPrueba>(json, Opciones)
            ?? throw new InvalidOperationException($"El archivo de datos de prueba está vacío: {ruta}");
        return new RepositorioDatosPrueba(datos);
    }

    public static IReadOnlyList<(T Elemento, double Puntaje)> Buscar<T>(IEnumerable<T> elementos, string consulta, Func<T, string> texto, int maximo)
    {
        var palabrasConsulta = Palabras(consulta);
        if (palabrasConsulta.Count == 0) return [];

        return elementos
            .Select(e => (Elemento: e, Puntaje: (double)Palabras(texto(e)).Intersect(palabrasConsulta).Count() / palabrasConsulta.Count))
            .Where(x => x.Puntaje > 0)
            .OrderByDescending(x => x.Puntaje)
            .Take(maximo)
            .ToList();
    }

    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "los", "las", "del", "que", "con", "por", "para", "una", "uno", "unos", "unas", "como", "pero",
        "sin", "sobre", "este", "esta", "esto", "ese", "esa", "hay", "muy", "mas", "cuando", "desde",
    };

    /// <summary>Palabras en minúsculas y sin tildes, sin palabras vacías ni términos de menos de 3 letras.</summary>
    private static HashSet<string> Palabras(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var limpio = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            limpio.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return limpio.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 3 && !PalabrasVacias.Contains(p))
            .ToHashSet(StringComparer.Ordinal);
    }
}
