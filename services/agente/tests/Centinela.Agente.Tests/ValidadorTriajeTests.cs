using System.Text.Json;
using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Tests;

public class ValidadorTriajeTests
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal) { "TK-1001", "KB-002" };

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

    private static string Modificar(Action<Dictionary<string, object>> cambio)
    {
        var datos = JsonSerializer.Deserialize<Dictionary<string, object>>(Respuestas.TriajeVpn())!;
        cambio(datos);
        return JsonSerializer.Serialize(datos);
    }
}
