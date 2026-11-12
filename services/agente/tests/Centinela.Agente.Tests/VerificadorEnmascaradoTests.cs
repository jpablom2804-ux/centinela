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
    [InlineData("tel +506 2222 3333", VerificadorEnmascarado.Telefono)]
    public void Detecta_datos_sensibles(string texto, string tipoEsperado)
    {
        Assert.Contains(tipoEsperado, VerificadorEnmascarado.Detectar(texto));
    }

    [Theory]
    [InlineData("Mi cédula es [CEDULA] y mi correo [CORREO]")]
    [InlineData("La VPN falla desde el 2026-11-11 a las 08:30")]
    [InlineData("Igual que el TK-1001 y el artículo KB-002")]
    [InlineData("El monto fue 1250.00 colones")]
    [InlineData("Error 0x80070005 en la versión 10.0.404")]
    [InlineData("Ticket 3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public void No_marca_texto_enmascarado_ni_datos_comunes(string texto)
    {
        Assert.Empty(VerificadorEnmascarado.Detectar(texto));
    }
}
