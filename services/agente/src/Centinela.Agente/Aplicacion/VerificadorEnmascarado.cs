using System.Text.RegularExpressions;

namespace Centinela.Agente.Aplicacion;

/// <summary>
/// Segunda barrera antes del LLM: mesa-servicio ya enmascaró, pero el agente vuelve a revisar
/// todo lo que va a enviar. Es conservador a propósito: ante la duda, no se llama al modelo.
/// Devuelve solo el tipo de dato encontrado, nunca el valor (no debe terminar en logs).
/// </summary>
public static partial class VerificadorEnmascarado
{
    public const string Correo = "CORREO";
    public const string Cuenta = "CUENTA";
    public const string Tarjeta = "TARJETA";
    public const string Cedula = "CEDULA";
    public const string Telefono = "TELEFONO";

    public static IReadOnlySet<string> Detectar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var encontrados = new HashSet<string>(StringComparer.Ordinal);

        if (PatronCorreo().IsMatch(texto)) encontrados.Add(Correo);
        if (PatronIban().IsMatch(texto)) encontrados.Add(Cuenta);

        foreach (Match m in PatronSecuenciaLarga().Matches(texto))
        {
            encontrados.Add(EsLuhnValido(m.Value) ? Tarjeta : Cuenta);
        }

        if (PatronCedulaFisica().IsMatch(texto) || PatronCedulaJuridica().IsMatch(texto) || PatronDimex().IsMatch(texto))
        {
            encontrados.Add(Cedula);
        }

        if (PatronTelefono().IsMatch(texto)) encontrados.Add(Telefono);
        return encontrados;
    }

    private static bool EsLuhnValido(string valor)
    {
        var digitos = valor.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digitos.Length is < 13 or > 19) return false;

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

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex PatronCorreo();

    // IBAN de Costa Rica: CR + 20 dígitos, con o sin separadores.
    [GeneratedRegex(@"\bCR\d{2}(?:[ \-]?\d){18}\b", RegexOptions.IgnoreCase)]
    private static partial Regex PatronIban();

    // En los patrones numéricos, (?<!\w) y (?!\w) evitan marcar números pegados a letras (0x80070005, TK-1001a).
    // 13 a 20 dígitos seguidos (con espacios o guiones): tarjeta si pasa Luhn, si no, cuenta.
    [GeneratedRegex(@"(?<!\w)(?:\d[ \-]?){12,19}\d(?!\w)")]
    private static partial Regex PatronSecuenciaLarga();

    // Cédula física: 9 dígitos (1-2345-6789).
    [GeneratedRegex(@"(?<!\w)[1-9][ \-]?\d{4}[ \-]?\d{4}(?!\w)")]
    private static partial Regex PatronCedulaFisica();

    // Cédula jurídica: 10 dígitos que empiezan con 3 (3-101-123456).
    [GeneratedRegex(@"(?<!\w)3[ \-]?\d{3}[ \-]?\d{6}(?!\w)")]
    private static partial Regex PatronCedulaJuridica();

    // DIMEX: 11 o 12 dígitos.
    [GeneratedRegex(@"(?<!\w)\d{11,12}(?!\w)")]
    private static partial Regex PatronDimex();

    // Teléfono de Costa Rica: 8 dígitos que empiezan de 2 a 8, con +506 opcional.
    [GeneratedRegex(@"(?<!\w)(?:\+?506[ \-]?)?[2-8]\d{3}[ \-]?\d{4}(?!\w)")]
    private static partial Regex PatronTelefono();
}
