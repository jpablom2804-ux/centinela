using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Tests;

public class VerificadorEnmascaradoTests
{
    [Theory]
    [InlineData("Mi cédula es 1-0987-0654", VerificadorEnmascarado.Cedula)]
    [InlineData("cédula 109870654", VerificadorEnmascarado.Cedula)]
    [InlineData("Empresa 3-101-123456", VerificadorEnmascarado.Cedula)]
    [InlineData("tarjeta 4111 1111 1111 1111", VerificadorEnmascarado.Tarjeta)]
    [InlineData("tarjeta 4111-1111-1111-1111", VerificadorEnmascarado.Tarjeta)]
    [InlineData("cuenta 12345678901234567", VerificadorEnmascarado.Cuenta)]
    [InlineData("IBAN CR05015202001026284066", VerificadorEnmascarado.Cuenta)]
    [InlineData("escríbanme a persona.prueba@ejemplo.com", VerificadorEnmascarado.Correo)]
    [InlineData("llamar al 8888-7777", VerificadorEnmascarado.Telefono)]
    [InlineData("tel 2222 3333", VerificadorEnmascarado.Telefono)]
    [InlineData("contraseña: Hola123", VerificadorEnmascarado.Credencial)]
    [InlineData("la clave es Abc#2024", VerificadorEnmascarado.Credencial)]
    [InlineData("el OTP que me llegó es 482913", VerificadorEnmascarado.Codigo)]
    [InlineData("CVV 123", VerificadorEnmascarado.Codigo)]
    [InlineData("el servidor 10.20.30.40 no responde", VerificadorEnmascarado.Ip)]
    public void Detecta_datos_sensibles(string texto, string tipoEsperado)
    {
        Assert.Contains(tipoEsperado, VerificadorEnmascarado.Detectar(texto));
    }

    // A1: caso 19. Formatos pensados para evadir patrones exactos: todos deben detectarse.
    [Theory]
    [InlineData("tarjeta 4111.1111.1111.1111")]
    [InlineData("tarjeta 4111/1111/1111/1111")]
    [InlineData("tarjeta 4111  1111  1111  1111")]
    [InlineData("tarjeta 4111 1111 1111 1111")]
    [InlineData("tarjeta 4111​1111​1111​1111")]
    [InlineData("tarjeta ４１１１ １１１１ １１１１ １１１１")]
    [InlineData("cedula1-0987-0654")]
    [InlineData("cédula 1.0987.0654")]
    [InlineData("cédula 1–0987–0654")]
    [InlineData("tel 88 88 77 77")]
    [InlineData("tel +506 2222 3333")]
    [InlineData("correo persona [at] ejemplo punto com")]
    [InlineData("correo persona@ejemplo")]
    [InlineData("correo persona＠ejemplo.com")]
    [InlineData("PIN: 4821")]
    public void Detecta_formatos_evasivos(string texto)
    {
        Assert.NotEmpty(VerificadorEnmascarado.Detectar(texto));
    }

    [Theory]
    [InlineData("Mi cédula es [CEDULA] y mi correo [CORREO]")]
    [InlineData("La VPN falla desde el 2026-11-11 a las 08:30")]
    [InlineData("Igual que el TK-1001 y el artículo KB-002")]
    [InlineData("El monto fue 1250.00 colones")]
    [InlineData("El pago fue de ₡12 500 000")]
    [InlineData("Instalé la versión 10.0.404")]
    [InlineData("Olvidé mi contraseña de la VPN")]
    [InlineData("Dice que la contraseña es incorrecta.")]
    [InlineData("Ticket 3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public void No_marca_texto_enmascarado_ni_datos_comunes(string texto)
    {
        Assert.Empty(VerificadorEnmascarado.Detectar(texto));
    }

    [Fact]
    public void En_resultados_json_revisa_los_textos_ya_decodificados()
    {
        // El serializador escapa caracteres: "４…" no debe esconder una tarjeta de ancho completo.
        const string Json = """{"resultados":[{"solucion":"tarjeta ４１１１ 1111 1111 1111"}]}""";

        Assert.Contains(VerificadorEnmascarado.Tarjeta, VerificadorEnmascarado.DetectarEnContenido(Json));
    }
}
