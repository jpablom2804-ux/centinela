using System.Text.Json;
using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Tests;

public class ValidadorTriajeTests
{
    private static readonly IdsVistos Ids = new(
        new HashSet<string>(StringComparer.Ordinal) { "TK-1001" },
        new HashSet<string>(StringComparer.Ordinal) { "KB-002" });

    [Fact]
    public void Acepta_un_triaje_valido()
    {
        var resultado = ValidadorTriaje.Validar(Respuestas.TriajeVpn(duplicados: ["TK-1001"], articulos: ["KB-002"]), Ids);

        Assert.True(resultado.EsValido);
        Assert.Equal("vpn", resultado.Sugerencia!.SistemaAfectado);
    }

    [Fact]
    public void Rechaza_propiedades_extra()
    {
        var json = Respuestas.TriajeVpn()[..^1] + ""","aprobado":true}""";

        Assert.False(ValidadorTriaje.Validar(json, Ids).EsValido);
    }

    [Fact]
    public void Rechaza_justificacion_demasiado_larga()
    {
        var json = Modificar(j => j["justificacion"] = new string('a', 501));

        Assert.False(ValidadorTriaje.Validar(json, Ids).EsValido);
    }

    [Fact]
    public void Rechaza_mas_de_cinco_ids()
    {
        var json = Respuestas.TriajeVpn(duplicados: ["TK-1001", "TK-1001", "TK-1001", "TK-1001", "TK-1001", "TK-1001"]);

        Assert.False(ValidadorTriaje.Validar(json, Ids).EsValido);
    }

    [Fact]
    public void Rechaza_caracteres_de_control()
    {
        var json = Modificar(j => j["borradorRespuesta"] = "Hola\u0007 mundo");

        Assert.False(ValidadorTriaje.Validar(json, Ids).EsValido);
    }

    // A15: caso 30 (enlaces de phishing y caracteres invisibles o bidireccionales en el borrador).
    [Theory]
    [InlineData("Restablece tu clave en http://banco-seguro.example/clave hoy.")]
    [InlineData("Restablece tu clave en https://evil.test/x")]
    [InlineData("Entra a www.banco-clave.com para continuar.")]
    [InlineData("Entra a banco-clave.com/restablecer para continuar.")]
    [InlineData("Haz clic [aquí](https://evil.test) para seguir.")]
    [InlineData("Hola‮ y gracias")]
    [InlineData("Hola​ y gracias")]
    public void Rechaza_enlaces_y_caracteres_invisibles_en_el_borrador(string borrador)
    {
        var json = Modificar(j => j["borradorRespuesta"] = borrador);

        Assert.False(ValidadorTriaje.Validar(json, Ids).EsValido);
    }

    [Fact]
    public void Acepta_enlaces_de_la_lista_blanca()
    {
        var json = Modificar(j => j["borradorRespuesta"] = "Revisa la guía en https://intranet.banco.example/vpn y avísanos.");

        Assert.True(ValidadorTriaje.Validar(json, Ids, ["banco.example"]).EsValido);
        Assert.False(ValidadorTriaje.Validar(json, Ids).EsValido);
    }

    // A28: caso 39 (un artículo como duplicado, o un ticket como artículo).
    [Fact]
    public void Cada_lista_se_valida_contra_los_ids_de_su_herramienta()
    {
        var articuloComoDuplicado = Respuestas.TriajeVpn(duplicados: ["KB-002"]);
        var ticketComoArticulo = Respuestas.TriajeVpn(articulos: ["TK-1001"]);

        Assert.False(ValidadorTriaje.Validar(articuloComoDuplicado, Ids).EsValido);
        Assert.False(ValidadorTriaje.Validar(ticketComoArticulo, Ids).EsValido);
    }

    private static string Modificar(Action<Dictionary<string, object>> cambio)
    {
        var datos = JsonSerializer.Deserialize<Dictionary<string, object>>(Respuestas.TriajeVpn())!;
        cambio(datos);
        return JsonSerializer.Serialize(datos);
    }
}
