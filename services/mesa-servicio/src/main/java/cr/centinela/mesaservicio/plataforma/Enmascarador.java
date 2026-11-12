package cr.centinela.mesaservicio.plataforma;

import java.text.Normalizer;
import java.util.EnumSet;
import java.util.Set;
import java.util.function.Function;
import java.util.regex.MatchResult;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Primera barrera antes de que un texto salga de mesa-servicio (evento al agente, API interna, embeddings):
 * reemplaza cédulas, cuentas, tarjetas, correos, teléfonos, credenciales, códigos e IPs por marcadores
 * como [CEDULA].
 *
 * <p>Usa las mismas reglas que {@code VerificadorEnmascarado} del agente (segunda barrera), con la misma
 * normalización: si este texto no pasara esa verificación, el agente no llamaría al modelo. El archivo
 * {@code tests/contratos/enmascarado.json} lo prueban los dos servicios para que no se separen.
 */
public final class Enmascarador {

    /** Si después de enmascarar todavía se detecta algo, no se arriesga: el texto se retiene completo. */
    public static final String TEXTO_RETENIDO = "[TEXTO_RETENIDO]";
    private static final int MAX_PASADAS = 3;

    public enum Tipo { CORREO, CUENTA, TARJETA, CEDULA, TELEFONO, CREDENCIAL, CODIGO, IP;

        String marcador() {
            return "[" + name() + "]";
        }
    }

    // UNICODE_CHARACTER_CLASS: \w, \b y \s como en .NET (por ejemplo "josé@banco" es un correo entero).
    private static final int UNICODE = Pattern.UNICODE_CHARACTER_CLASS;
    private static final int SIN_MAYUSCULAS = Pattern.CASE_INSENSITIVE | Pattern.UNICODE_CASE | UNICODE;

    // El agente detecta hasta el dominio; aquí se enmascara también el resto (".cr"), sin dejar pedazos.
    private static final Pattern CORREO = Pattern.compile(
            "[\\w.%+\\-]+\\s*(?:@|\\[at\\]|\\(at\\)|\\sarroba\\s)\\s*[\\w\\-]+(?:\\.[\\w\\-]+)*", SIN_MAYUSCULAS);
    private static final Pattern IBAN = Pattern.compile("CR\\s?\\d{2}(?:[\\s.\\-/]?\\d){18}", SIN_MAYUSCULAS);
    private static final Pattern CREDENCIAL = Pattern.compile(
            "\\b(?:contrase(?:ñ|n)a|password|passwd|pwd|clave|usuario y clave)\\b\\s*"
                    + "(?:[:=]\\s*\\S{3,}|es\\s+(?=\\S*?[\\d_@#$%&*!?¿¡+=])\\S{4,})", SIN_MAYUSCULAS);
    private static final Pattern CODIGO = Pattern.compile(
            "\\b(?:pin|otp|cvv|cvc|cvv2|token|c(?:ó|o)digo\\s+de\\s+(?:verificaci(?:ó|o)n|seguridad|acceso)"
                    + "|c(?:ó|o)digo\\s+(?:otp|sms))\\b\\D{0,30}?\\d{3,8}(?!\\d)", SIN_MAYUSCULAS);
    private static final Pattern IP = Pattern.compile("(?<![\\d.])(?:\\d{1,3}\\.){3}\\d{1,3}(?![\\d.])", UNICODE);
    // 8 o más dígitos con hasta 3 separadores entre cada uno; pegados a letras también cuentan.
    private static final Pattern DIGITOS = Pattern.compile("(?<!\\d)\\d(?:[\\s.\\-/_]{0,3}\\d){7,}(?!\\d)", UNICODE);
    private static final Pattern FECHA = Pattern.compile(
            "^(?:\\d{4}[\\-/.]\\d{1,2}[\\-/.]\\d{1,2}|\\d{1,2}[\\-/.]\\d{1,2}[\\-/.]\\d{4})$");
    private static final Pattern MONEDA = Pattern.compile("(?:₡|\\$|CRC|USD|colones)\\s*$", SIN_MAYUSCULAS);

    private Enmascarador() {}

    /** Devuelve el texto normalizado y enmascarado; null se mantiene null. */
    public static String enmascarar(String texto) {
        if (texto == null) {
            return null;
        }
        var resultado = texto;
        for (var i = 0; i < MAX_PASADAS; i++) {
            resultado = pasada(resultado);
            if (detectar(resultado).isEmpty()) {
                return resultado;
            }
        }
        return TEXTO_RETENIDO;
    }

    /** Tipos de dato sensible que quedan en el texto. Nunca devuelve el valor (no debe terminar en logs). */
    public static Set<Tipo> detectar(String texto) {
        var normalizado = normalizar(texto);
        var encontrados = EnumSet.noneOf(Tipo.class);
        if (CORREO.matcher(normalizado).find()) encontrados.add(Tipo.CORREO);
        if (IBAN.matcher(normalizado).find()) encontrados.add(Tipo.CUENTA);
        if (CREDENCIAL.matcher(normalizado).find()) encontrados.add(Tipo.CREDENCIAL);
        if (CODIGO.matcher(normalizado).find()) encontrados.add(Tipo.CODIGO);
        if (IP.matcher(normalizado).find()) encontrados.add(Tipo.IP);
        var digitos = DIGITOS.matcher(normalizado);
        while (digitos.find()) {
            var tipo = clasificarDigitos(normalizado, digitos);
            if (tipo != null) encontrados.add(tipo);
        }
        return encontrados;
    }

    /**
     * NFKC, sin caracteres de formato invisibles (Unicode Cf), espacios y guiones unificados y dígitos de
     * otros alfabetos pasados a ASCII: "８８８８", "1​2345" o "8888–1234" no esconden nada.
     */
    public static String normalizar(String texto) {
        var nfkc = Normalizer.normalize(texto, Normalizer.Form.NFKC);
        var limpio = new StringBuilder(nfkc.length());
        nfkc.codePoints().forEach(c -> {
            switch (Character.getType(c)) {
                case Character.FORMAT -> { }
                case Character.SPACE_SEPARATOR -> limpio.append(' ');
                case Character.DASH_PUNCTUATION -> limpio.append('-');
                case Character.DECIMAL_DIGIT_NUMBER -> limpio.append((char) ('0' + Character.digit(c, 10)));
                default -> limpio.appendCodePoint(c);
            }
        });
        return limpio.toString();
    }

    private static String pasada(String texto) {
        var t = normalizar(texto);
        t = reemplazar(CORREO, t, m -> Tipo.CORREO.marcador());
        t = reemplazar(IBAN, t, m -> Tipo.CUENTA.marcador());
        t = reemplazar(CREDENCIAL, t, m -> Tipo.CREDENCIAL.marcador());
        t = reemplazar(CODIGO, t, m -> Tipo.CODIGO.marcador());
        t = reemplazar(IP, t, m -> Tipo.IP.marcador());
        var actual = t;
        return reemplazar(DIGITOS, t, m -> {
            var tipo = clasificarDigitos(actual, m);
            return tipo == null ? m.group() : tipo.marcador();
        });
    }

    private static String reemplazar(Pattern patron, String texto, Function<MatchResult, String> reemplazo) {
        return patron.matcher(texto).replaceAll(m -> Matcher.quoteReplacement(reemplazo.apply(m)));
    }

    /** Fechas y montos no son datos personales; el resto se clasifica por cantidad de dígitos. */
    private static Tipo clasificarDigitos(String texto, MatchResult m) {
        var valor = m.group();
        if (FECHA.matcher(valor).matches() || MONEDA.matcher(texto.substring(Math.max(0, m.start() - 5), m.start())).find()) {
            return null;
        }
        var digitos = (int) valor.chars().filter(Character::isDigit).count();
        if (digitos == 8) return Tipo.TELEFONO;
        if (digitos <= 12) return Tipo.CEDULA;
        if (digitos <= 19) return luhnValido(valor) ? Tipo.TARJETA : Tipo.CUENTA;
        return Tipo.CUENTA;
    }

    private static boolean luhnValido(String valor) {
        var digitos = valor.chars().filter(Character::isDigit).map(c -> c - '0').toArray();
        var suma = 0;
        for (var i = 0; i < digitos.length; i++) {
            var d = digitos[digitos.length - 1 - i];
            if (i % 2 == 1) {
                d *= 2;
                if (d > 9) d -= 9;
            }
            suma += d;
        }
        return suma % 10 == 0;
    }
}
