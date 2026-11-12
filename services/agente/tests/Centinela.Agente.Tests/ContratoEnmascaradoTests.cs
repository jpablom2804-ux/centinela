using System.Text.Json;
using Centinela.Agente.Aplicacion;

namespace Centinela.Agente.Tests;

/// <summary>
/// Lo que mesa-servicio deja enmascarado (tests/contratos/enmascarado.json) tiene que pasar la verificación
/// del agente; si no, el agente nunca llamaría al modelo. Y lo que venía sin enmascarar tiene que detectarse.
/// </summary>
public class ContratoEnmascaradoTests
{
    private sealed record Caso(string Nombre, string Entrada, string Esperado);

    private sealed record Contrato(string Descripcion, List<Caso> Casos);

    public static TheoryData<string> Nombres()
    {
        var datos = new TheoryData<string>();
        foreach (var caso in Cargar().Casos) datos.Add(caso.Nombre);
        return datos;
    }

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNameCaseInsensitive = true };

    private static Contrato Cargar()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "contratos", "enmascarado.json");
        return JsonSerializer.Deserialize<Contrato>(File.ReadAllText(ruta), Opciones)
               ?? throw new InvalidOperationException("Contrato vacío");
    }

    [Theory]
    [MemberData(nameof(Nombres))]
    public void LoQueEnmascaraMesaServicioPasaLaVerificacion(string nombre)
    {
        var caso = Cargar().Casos.Single(c => c.Nombre == nombre);

        Assert.Empty(VerificadorEnmascarado.Detectar(caso.Esperado));
        if (caso.Entrada != caso.Esperado)
        {
            Assert.NotEmpty(VerificadorEnmascarado.Detectar(caso.Entrada));
        }
    }
}
