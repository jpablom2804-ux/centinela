using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// Segunda barrera antes del LLM: mesa-servicio ya enmascaró, pero el agente vuelve a revisar
/// todo lo que va a enviar. Es conservador a propósito: ante la duda, no se llama al modelo.
/// Devuelve solo el tipo de dato encontrado, nunca el valor (no debe terminar en logs).
/// Para no depender del formato (A1), primero normaliza el texto y después busca por cantidad
/// de dígitos en una ventana, no por patrones exactos.
/// </summary>
public static partial class VerificadorEnmascarado
{
    public const string Correo = "CORREO";
    public const string Cuenta = "CUENTA";
    public const string Tarjeta = "TARJETA";
    public const string Cedula = "CEDULA";
    public const string Telefono = "TELEFONO";
    public const string Credencial = "CREDENCIAL";
    public const string Codigo = "CODIGO";
    public const string Ip = "IP";

    public static IReadOnlySet<string> Detectar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var normalizado = Normalizar(texto);
        var encontrados = new HashSet<string>(StringComparer.Ordinal);

        if (PatronCorreo().IsMatch(normalizado)) encontrados.Add(Correo);
        if (PatronIban().IsMatch(normalizado)) encontrados.Add(Cuenta);
        if (PatronCredencial().IsMatch(normalizado)) encontrados.Add(Credencial);
        if (PatronCodigo().IsMatch(normalizado)) encontrados.Add(Codigo);
        if (PatronIp().IsMatch(normalizado)) encontrados.Add(Ip);

        foreach (Match m in PatronDigitos().Matches(normalizado))
        {
            if (EsFecha(m.Value) || EsMonto(normalizado, m.Index)) continue;
            var tipo = Clasificar(m.Value);
            if (tipo is not null) encontrados.Add(tipo);
        }

        return encontrados;
    }

    /// <summary>
    /// Revisa un resultado de herramienta. Si es JSON, revisa cada texto ya decodificado: el serializador
    /// escapa caracteres (１…) y un dato no debe esconderse detrás de un escape.
    /// </summary>
    public static IReadOnlySet<string> DetectarEnContenido(string contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(contenido);
        }
        catch (JsonException)
        {
            return Detectar(contenido);
        }

        var encontrados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (clave, texto) in Textos(null, raiz))
        {
            if (EsValorEstructural(clave, texto)) continue;
            encontrados.UnionWith(Detectar(texto));
        }

        return encontrados;
    }

    /// <summary>
    /// IDs y fechas que generan los sistemas (UUID, KB-001, 2026-11-13T01:06:16.989186Z) tienen tramos de dígitos que
    /// parecen teléfonos o cédulas. Se exceptúan solo si la clave es de ID o de fecha Y el valor tiene exactamente ese
    /// formato: un dato personal no cabe en un UUID o una fecha exactos, y cualquier otro texto se revisa igual.
    /// </summary>
    private static bool EsValorEstructural(string? clave, string valor) => clave switch
    {
        "id" or "ticketId" => Guid.TryParseExact(valor, "D", out _) || PatronCodigoInterno().IsMatch(valor),
        "desde" or "creadoEn" or "ocurridoEn" or "recibidaEn" => PatronInstante().IsMatch(valor),
        _ => false,
    };

    /// <summary>NFKC, sin caracteres de formato invisibles (Unicode Cf) y con separadores unificados.</summary>
    public static string Normalizar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var nfkc = texto.Normalize(NormalizationForm.FormKC);
        var limpio = new StringBuilder(nfkc.Length);
        foreach (var original in nfkc)
        {
            // Con InvariantGlobalization, .NET no aplica NFKC: el ancho completo (U+FF01–U+FF5E) y los dígitos
            // de otros alfabetos se convierten a mano a ASCII, para que "４１１１" o "＠" no pasen.
            var c = original is >= '！' and <= '～' ? (char)(original - 0xFEE0) : original;
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.Format) continue;
            limpio.Append(categoria switch
            {
                UnicodeCategory.SpaceSeparator => ' ',
                UnicodeCategory.DashPunctuation => '-',
                UnicodeCategory.DecimalDigitNumber when !char.IsAsciiDigit(c) => (char)('0' + (int)char.GetNumericValue(c)),
                _ => c,
            });
        }

        return limpio.ToString();
    }

    /// <summary>Cada texto del JSON (claves incluidas) con la clave de la que cuelga, ya decodificado.</summary>
    private static IEnumerable<(string? Clave, string Texto)> Textos(string? clave, JsonNode? nodo) => nodo switch
    {
        JsonObject o => o.SelectMany(p => Textos(p.Key, p.Value).Prepend((null, p.Key))),
        JsonArray a => a.SelectMany(n => Textos(clave, n)),
        JsonValue v when v.TryGetValue<string>(out var s) => [(clave, s)],
        JsonValue v => [(clave, v.ToJsonString())],
        _ => [],
    };

    // Códigos de los propios sistemas: KB-001, TK-1001, INC-501.
    [GeneratedRegex(@"^[A-Z]{2,4}-\d{3,6}$")]
    private static partial Regex PatronCodigoInterno();

    // ISO-8601 completo con zona: 2026-11-13T01:06:16.989186Z o 2026-11-13T01:06:16+00:00.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex PatronInstante();

    private static string? Clasificar(string valor)
    {
        var digitos = valor.Count(char.IsAsciiDigit);
        return digitos switch
        {
            8 => Telefono,
            9 or 10 or 11 or 12 => Cedula,
            >= 13 and <= 19 => EsLuhnValido(valor) ? Tarjeta : Cuenta,
            >= 20 => Cuenta,
            _ => null,
        };
    }

    private static bool EsFecha(string valor) => PatronFecha().IsMatch(valor);

    // "₡12 500 000" o "USD 1 250 000" son montos, no teléfonos ni cédulas.
    private static bool EsMonto(string texto, int inicio)
    {
        var antes = texto[Math.Max(0, inicio - 5)..inicio];
        return PatronMoneda().IsMatch(antes);
    }

    private static bool EsLuhnValido(string valor)
    {
        var digitos = valor.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        var suma = 0;
        for (var i = 0; i < digitos.Length; i++)
        {
            var d = digitos[digitos.Length - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }

            suma += d;
        }

        return suma % 10 == 0;
    }

    // Correo con "@" (ya normalizado) o escrito "[at]" / "(at)" / " arroba ", con o sin dominio de nivel superior.
    [GeneratedRegex(@"[\w.%+\-]+\s*(?:@|\[at\]|\(at\)|\sarroba\s)\s*[\w\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex PatronCorreo();

    [GeneratedRegex(@"CR\s?\d{2}(?:[\s.\-/]?\d){18}", RegexOptions.IgnoreCase)]
    private static partial Regex PatronIban();

    // Secuencias de 8 o más dígitos con hasta 3 separadores entre cada uno (espacios, puntos, guiones,
    // barras). Pegadas a letras también cuentan ("cedula1-0987-0654"): ante la duda, se marca.
    [GeneratedRegex(@"(?<!\d)\d(?:[\s.\-/_]{0,3}\d){7,}(?!\d)")]
    private static partial Regex PatronDigitos();

    [GeneratedRegex(@"^(?:\d{4}[\-/.]\d{1,2}[\-/.]\d{1,2}|\d{1,2}[\-/.]\d{1,2}[\-/.]\d{4})$")]
    private static partial Regex PatronFecha();

    [GeneratedRegex(@"(?:₡|\$|CRC|USD|colones)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PatronMoneda();

    // "contraseña: X", "password=X" o "la clave es Abc123": hace falta ":"/"=" o un valor con dígito o símbolo,
    // para no marcar "olvidé mi contraseña".
    [GeneratedRegex(@"\b(?:contrase(?:ñ|n)a|password|passwd|pwd|clave|usuario y clave)\b\s*(?:[:=]\s*\S{3,}|es\s+(?=\S*?[\d_@#$%&*!?¿¡+=])\S{4,})", RegexOptions.IgnoreCase)]
    private static partial Regex PatronCredencial();

    [GeneratedRegex(@"\b(?:pin|otp|cvv|cvc|cvv2|token|c(?:ó|o)digo\s+de\s+(?:verificaci(?:ó|o)n|seguridad|acceso)|c(?:ó|o)digo\s+(?:otp|sms))\b\D{0,30}?\d{3,8}(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex PatronCodigo();

    [GeneratedRegex(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")]
    private static partial Regex PatronIp();
}
