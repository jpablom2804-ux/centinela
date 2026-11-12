using System.Text.Json;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Busca tickets resueltos parecidos, para detectar duplicados y reutilizar soluciones.</summary>
public sealed class HerramientaBuscarTicketsSimilares(RepositorioDatosPrueba repositorio) : IHerramienta
{
    public const string Nombre = "buscar_tickets_similares";

    public DefinicionHerramienta Definicion { get; } = new(
        Nombre,
        "Busca tickets ya resueltos que se parecen al problema descrito. Úsala al empezar el triaje, " +
        "con las palabras clave del problema, para encontrar posibles duplicados y soluciones anteriores. " +
        $"Devuelve como máximo {EntradaHerramienta.MaxResultados} tickets.",
        EntradaHerramienta.EsquemaTexto("consulta", "Palabras clave del problema, por ejemplo: vpn no conecta desde casa"));

    // [FLUJO TRIAJE_AGENTE 6b] Valida la entrada del modelo y busca en los datos de prueba → resultado como dato no confiable
    public Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
    {
        var (consulta, error) = EntradaHerramienta.LeerTexto(entrada, "consulta", 3, 300);
        if (consulta is null) return Task.FromResult(ResultadoEjecucion.Error(error!));

        var encontrados = RepositorioDatosPrueba.Buscar(
                repositorio.Datos.TicketsResueltos,
                consulta,
                t => $"{t.Titulo} {t.Descripcion} {t.Sistema}",
                EntradaHerramienta.MaxResultados)
            .Select(x => new
            {
                id = x.Elemento.Id,
                titulo = x.Elemento.Titulo,
                categoria = x.Elemento.Categoria,
                sistema = x.Elemento.Sistema,
                solucion = EntradaHerramienta.Recortar(x.Elemento.Solucion, 500),
                similitud = Math.Round(x.Puntaje, 2),
            })
            .ToList();

        return Task.FromResult(new ResultadoEjecucion(
            EntradaHerramienta.Resultado(encontrados), EsError: false, encontrados.Select(e => e.id).ToList(), TipoIdDevuelto.Ticket));
    }
}
