using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Dominio;

namespace Centinela.Agente.Aplicacion;

public sealed record ResultadoValidacion(TriajeSugerido? Sugerencia, IReadOnlyList<string> Errores)
{
    public bool EsValido => Sugerencia is not null;
}

/// <summary>
/// Valida la respuesta final del modelo. El esquema de la API garantiza la forma, pero no los
/// largos ni que los IDs existan: eso se revisa aquí. Si algo falla, el triaje es inválido.
/// </summary>
public static class ValidadorTriaje
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
    public static ResultadoValidacion Validar(string texto, IReadOnlySet<string> idsPermitidos)
    {
        ArgumentNullException.ThrowIfNull(idsPermitidos);
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
        var justificacion = TextoLibre(raiz, "justificacion", LimitesSalida.MaxJustificacion, errores);
        var borrador = TextoLibre(raiz, "borradorRespuesta", LimitesSalida.MaxBorradorRespuesta, errores);
        var duplicados = ListaDeIds(raiz, "duplicadosPosibles", idsPermitidos, errores);
        var articulos = ListaDeIds(raiz, "articulosSugeridos", idsPermitidos, errores);

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

    private static string? TextoLibre(JsonElement raiz, string nombre, int maximo, List<string> errores)
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

        return texto;
    }

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
