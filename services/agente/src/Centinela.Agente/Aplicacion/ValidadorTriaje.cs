using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Centinela.Agente.Dominio;

namespace Centinela.Agente.Aplicacion;

public sealed record ResultadoValidacion(TriajeSugerido? Sugerencia, IReadOnlyList<string> Errores)
{
    public bool EsValido => Sugerencia is not null;
}

/// <summary>
/// IDs que las herramientas mostraron en esta conversación, separados por tipo (A28): un artículo no
/// puede sugerirse como duplicado ni un ticket como artículo.
/// </summary>
public sealed record IdsVistos(IReadOnlySet<string> Tickets, IReadOnlySet<string> Articulos);

/// <summary>
/// Valida la respuesta final del modelo. El esquema de la API garantiza la forma, pero no los
/// largos ni que los IDs existan: eso se revisa aquí. Si algo falla, el triaje es inválido.
/// </summary>
public static partial class ValidadorTriaje
{
    private static readonly string[] Propiedades =
    [
        "categoria", "sistemaAfectado", "prioridadSugerida", "justificacion",
        "duplicadosPosibles", "articulosSugeridos", "borradorRespuesta",
    ];

    /// <summary>Esquema que se le pide a la API para la respuesta final.</summary>
    public static JsonObject Esquema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray(Propiedades.Select(p => (JsonNode)p).ToArray()),
        ["properties"] = new JsonObject
        {
            ["categoria"] = Enumeracion(Catalogos.Categorias),
            ["sistemaAfectado"] = Enumeracion(Catalogos.Sistemas),
            ["prioridadSugerida"] = Enumeracion(Catalogos.Prioridades),
            ["justificacion"] = new JsonObject { ["type"] = "string", ["description"] = $"Por qué esa prioridad, en máximo {LimitesSalida.MaxJustificacion} caracteres." },
            ["duplicadosPosibles"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["articulosSugeridos"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["borradorRespuesta"] = new JsonObject { ["type"] = "string", ["description"] = $"Respuesta para quien reportó, en máximo {LimitesSalida.MaxBorradorRespuesta} caracteres." },
        },
    };

    // [FLUJO TRIAJE_AGENTE 7] Valida el JSON final contra catálogos, largos e IDs vistos → completado o invalido
    public static ResultadoValidacion Validar(string texto, IdsVistos vistos, IReadOnlyCollection<string>? dominiosPermitidos = null)
    {
        ArgumentNullException.ThrowIfNull(vistos);
        var dominios = dominiosPermitidos ?? [];
        JsonElement raiz;
        try
        {
            using var documento = JsonDocument.Parse(texto);
            raiz = documento.RootElement.Clone();
        }
        catch (JsonException e)
        {
            // Solo la forma del error, nunca el contenido: es texto generado por el modelo.
            var inicio = texto.Length == 0 ? "vacía" : texto[0] is '{' or '[' or '"' ? $"empieza con '{texto[0]}'" : "empieza con texto";
            return Invalido($"la respuesta no es JSON ({texto.Length} caracteres, {inicio}, error en la posición {e.BytePositionInLine})");
        }

        if (raiz.ValueKind != JsonValueKind.Object) return Invalido("la respuesta no es un objeto JSON");

        var errores = new List<string>();
        var nombres = raiz.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!nombres.SetEquals(Propiedades)) errores.Add("propiedades distintas a las del contrato");
        if (errores.Count > 0) return new(null, errores);

        var categoria = TextoDeCatalogo(raiz, "categoria", Catalogos.Categorias, errores);
        var sistema = TextoDeCatalogo(raiz, "sistemaAfectado", Catalogos.Sistemas, errores);
        var prioridad = TextoDeCatalogo(raiz, "prioridadSugerida", Catalogos.Prioridades, errores);
        var justificacion = TextoLibre(raiz, "justificacion", LimitesSalida.MaxJustificacion, dominios, errores);
        var borrador = TextoLibre(raiz, "borradorRespuesta", LimitesSalida.MaxBorradorRespuesta, dominios, errores);
        var duplicados = ListaDeIds(raiz, "duplicadosPosibles", vistos.Tickets, errores);
        var articulos = ListaDeIds(raiz, "articulosSugeridos", vistos.Articulos, errores);

        if (errores.Count > 0) return new(null, errores);
        return new(new TriajeSugerido(categoria!, sistema!, prioridad!, justificacion!, duplicados!, articulos!, borrador!), []);
    }

    private static ResultadoValidacion Invalido(string error) => new(null, [error]);

    private static JsonObject Enumeracion(IEnumerable<string> valores) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(valores.Select(v => (JsonNode)v).ToArray()),
    };

    private static string? TextoDeCatalogo(JsonElement raiz, string nombre, IReadOnlyList<string> catalogo, List<string> errores)
    {
        var valor = raiz.GetProperty(nombre);
        if (valor.ValueKind == JsonValueKind.String && catalogo.Contains(valor.GetString()!)) return valor.GetString();
        errores.Add($"{nombre} fuera del catálogo");
        return null;
    }

    private static string? TextoLibre(JsonElement raiz, string nombre, int maximo, IReadOnlyCollection<string> dominios, List<string> errores)
    {
        var valor = raiz.GetProperty(nombre);
        var texto = valor.ValueKind == JsonValueKind.String ? valor.GetString()! : null;
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > maximo)
        {
            errores.Add($"{nombre} vacío o de más de {maximo} caracteres");
            return null;
        }

        // Texto plano: sin caracteres de control fuera de saltos de línea y tabulaciones.
        if (texto.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
        {
            errores.Add($"{nombre} con caracteres de control");
            return null;
        }

        // A15: sin caracteres invisibles ni de control bidireccional (U+200B, U+202E…), que engañan a quien aprueba.
        if (texto.Any(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        {
            errores.Add($"{nombre} con caracteres invisibles o de control bidireccional");
            return null;
        }

        // A15: ningún enlace fuera de la lista blanca (Agente:DominiosPermitidos). Un ticket envenenado no puede
        // hacer que el borrador mande a otro usuario a un sitio de phishing.
        foreach (Match url in PatronUrl().Matches(texto))
        {
            if (!DominioPermitido(url.Groups["host"].Value, dominios))
            {
                errores.Add($"{nombre} con un enlace fuera de la lista blanca");
                return null;
            }
        }

        return texto;
    }

    private static bool DominioPermitido(string host, IReadOnlyCollection<string> dominios) =>
        dominios.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase)
                          || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));

    // Con esquema (http://, https://), con www. o un dominio con terminación conocida ("banco-seguro.com/clave").
    [GeneratedRegex(@"(?:https?://|www\.)(?<host>[^\s/:?#)\]>""']+)|(?<![\w.@])(?<host>(?:[a-z0-9\-]+\.)+(?:com|net|org|io|co|cr|info|biz|xyz|me|app|dev|site|online|link|click|top|ly|ru|cn|tk))\b", RegexOptions.IgnoreCase)]
    private static partial Regex PatronUrl();

    private static List<string>? ListaDeIds(JsonElement raiz, string nombre, IReadOnlySet<string> permitidos, List<string> errores)
    {
        var valor = raiz.GetProperty(nombre);
        if (valor.ValueKind != JsonValueKind.Array || valor.GetArrayLength() > LimitesSalida.MaxIds)
        {
            errores.Add($"{nombre} no es una lista de hasta {LimitesSalida.MaxIds} IDs");
            return null;
        }

        var ids = new List<string>();
        foreach (var item in valor.EnumerateArray())
        {
            var id = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (id is null || !permitidos.Contains(id) || ids.Contains(id))
            {
                errores.Add($"{nombre} con un ID que no salió de las herramientas o repetido");
                return null;
            }

            ids.Add(id);
        }

        return ids;
    }
}
