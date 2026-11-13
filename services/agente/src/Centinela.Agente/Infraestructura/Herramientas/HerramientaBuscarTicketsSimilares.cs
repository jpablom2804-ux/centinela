using System.Text.Json;
using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Busca tickets parecidos, para detectar duplicados y reutilizar soluciones.</summary>
public sealed class HerramientaBuscarTicketsSimilares(IConsultasMesaServicio consultas) : IHerramienta
{
    public const string Nombre = "buscar_tickets_similares";

    public DefinicionHerramienta Definicion { get; } = new(
        Nombre,
        "Busca tickets que se parecen al problema descrito: abiertos (posibles duplicados) y resueltos (con su solución). " +
        "Úsala al empezar el triaje, con las palabras clave del problema. " +
        $"Devuelve como máximo {EntradaHerramienta.MaxResultados} tickets.",
        EntradaHerramienta.EsquemaTexto("consulta", "Palabras clave del problema, por ejemplo: vpn no conecta desde casa"));

    // [FLUJO TRIAJE_AGENTE 6b] Valida la entrada del modelo y consulta (API interna o datos de prueba) → resultado como dato no confiable
    public async Task<ResultadoEjecucion> EjecutarAsync(JsonElement entrada, ContextoHerramienta contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var (consulta, error) = EntradaHerramienta.LeerTexto(entrada, "consulta", 3, 300);
        if (consulta is null) return ResultadoEjecucion.Error(error!);

        IReadOnlyList<TicketSimilar> tickets;
        try
        {
            // El ticket en curso lo pone el ciclo, nunca el modelo.
            tickets = await consultas.BuscarTicketsSimilaresAsync(contexto.TicketId, consulta, EntradaHerramienta.MaxResultados, ct);
        }
        catch (ConsultaNoDisponibleException)
        {
            return ResultadoEjecucion.Error(EntradaHerramienta.FuenteNoDisponible);
        }

        var encontrados = tickets
            .Take(EntradaHerramienta.MaxResultados)
            .Select(t => new
            {
                id = t.Id,
                titulo = EntradaHerramienta.Recortar(t.Titulo, 200),
                estado = t.Estado,
                categoria = t.Categoria,
                sistema = t.Sistema,
                solucion = t.Solucion is null ? null : EntradaHerramienta.Recortar(t.Solucion, 500),
                similitud = Math.Round(t.Similitud, 2),
            })
            .ToList();

        return new ResultadoEjecucion(
            EntradaHerramienta.Resultado(encontrados), EsError: false, encontrados.Select(e => e.id).ToList(), TipoIdDevuelto.Ticket);
    }
}
