using System.Text.Json;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Busca artículos de la base de conocimiento que ayuden a resolver el ticket.</summary>
public sealed class HerramientaBuscarArticulos(IConsultasMesaServicio consultas) : IHerramienta
{
    public const string Nombre = "buscar_articulos_conocimiento";

    public DefinicionHerramienta Definicion { get; } = new(
        Nombre,
        "Busca artículos de solución en la base de conocimiento. Úsala para sugerir al técnico pasos " +
        $"documentados. Devuelve como máximo {EntradaHerramienta.MaxResultados} artículos.",
        EntradaHerramienta.EsquemaTexto("consulta", "Palabras clave del problema, por ejemplo: restablecer contraseña directorio activo"));

    // [FLUJO TRIAJE_AGENTE 6b] Valida la entrada del modelo y consulta (API interna o datos de prueba) → resultado como dato no confiable
    public async Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
    {
        var (consulta, error) = EntradaHerramienta.LeerTexto(entrada, "consulta", 3, 300);
        if (consulta is null) return ResultadoEjecucion.Error(error!);

        IReadOnlyList<ArticuloEncontrado> articulos;
        try
        {
            articulos = await consultas.BuscarArticulosAsync(consulta, EntradaHerramienta.MaxResultados, ct);
        }
        catch (ConsultaNoDisponibleException)
        {
            return ResultadoEjecucion.Error(EntradaHerramienta.FuenteNoDisponible);
        }

        var encontrados = articulos
            .Take(EntradaHerramienta.MaxResultados)
            .Select(a => new
            {
                id = a.Id,
                titulo = EntradaHerramienta.Recortar(a.Titulo, 200),
                extracto = EntradaHerramienta.Recortar(a.Extracto, 600),
                similitud = Math.Round(a.Similitud, 2),
            })
            .ToList();

        return new ResultadoEjecucion(
            EntradaHerramienta.Resultado(encontrados), EsError: false, encontrados.Select(e => e.id).ToList(), TipoIdDevuelto.Articulo);
    }
}
