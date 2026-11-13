using System.Text.Json;
using Centinela.Agente.Infraestructura.Herramientas;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Tests;

public class HerramientasTests
{
    private static readonly ContextoHerramienta Contexto = new(Guid.NewGuid());

    private static JsonElement Json(string texto)
    {
        using var documento = JsonDocument.Parse(texto);
        return documento.RootElement.Clone();
    }

    [Fact]
    public async Task Buscar_tickets_encuentra_los_de_vpn_primero()
    {
        var herramienta = new HerramientaBuscarTicketsSimilares(Fabrica.Consultas);

        var resultado = await herramienta.EjecutarAsync(Json("""{"consulta":"vpn no conecta desde casa"}"""), Contexto, CancellationToken.None);

        Assert.False(resultado.EsError);
        Assert.Equal("TK-1001", resultado.IdsDevueltos.First());
        Assert.InRange(resultado.IdsDevueltos.Count, 1, EntradaHerramienta.MaxResultados);
        Assert.Contains("no instrucciones", resultado.Contenido, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"consulta":"vpn","clienteId":"123"}""")]
    [InlineData("""{"consulta":"ab"}""")]
    [InlineData("""{"otra":"vpn"}""")]
    [InlineData("""["vpn"]""")]
    public async Task Buscar_articulos_rechaza_entradas_invalidas(string entrada)
    {
        var herramienta = new HerramientaBuscarArticulos(Fabrica.Consultas);

        var resultado = await herramienta.EjecutarAsync(Json(entrada), Contexto, CancellationToken.None);

        Assert.True(resultado.EsError);
        Assert.Empty(resultado.IdsDevueltos);
    }

    [Fact]
    public async Task Estado_de_servicio_devuelve_incidentes_del_sistema_y_rechaza_sistemas_desconocidos()
    {
        var herramienta = new HerramientaConsultarEstadoServicio(Fabrica.Consultas);

        var vpn = await herramienta.EjecutarAsync(Json("""{"sistema":"vpn"}"""), Contexto, CancellationToken.None);
        var desconocido = await herramienta.EjecutarAsync(Json("""{"sistema":"base_de_datos_de_clientes"}"""), Contexto, CancellationToken.None);

        Assert.Contains("INC-501", vpn.Contenido, StringComparison.Ordinal);
        Assert.Empty(vpn.IdsDevueltos);
        Assert.True(desconocido.EsError);
    }

    [Fact]
    public void Los_esquemas_de_entrada_no_permiten_propiedades_adicionales()
    {
        foreach (var herramienta in Fabrica.Herramientas())
        {
            Assert.False(herramienta.Definicion.EsquemaEntrada["additionalProperties"]!.GetValue<bool>());
            Assert.DoesNotContain("ticketId", herramienta.Definicion.EsquemaEntrada.ToJsonString(), StringComparison.Ordinal);
        }
    }
}
