using Centinela.Agente.Puertos;

namespace Centinela.Agente.Infraestructura.Herramientas;

/// <summary>Datos sintéticos en memoria (fase 1): para el modo archivo y las pruebas, sin mesa-servicio.</summary>
public sealed class ConsultasDatosPrueba(RepositorioDatosPrueba repositorio) : IConsultasMesaServicio
{
    public Task<IReadOnlyList<TicketSimilar>> BuscarTicketsSimilaresAsync(Guid ticketEnCurso, string consulta, int limite, CancellationToken ct)
    {
        IReadOnlyList<TicketSimilar> encontrados = RepositorioDatosPrueba.Buscar(
                repositorio.Datos.TicketsResueltos, consulta, t => $"{t.Titulo} {t.Descripcion} {t.Sistema}", limite)
            .Select(x => new TicketSimilar(x.Elemento.Id, x.Elemento.Titulo, "resuelto", x.Elemento.Sistema, x.Elemento.Categoria,
                x.Elemento.Solucion, x.Puntaje))
            .ToList();
        return Task.FromResult(encontrados);
    }

    public Task<IReadOnlyList<ArticuloEncontrado>> BuscarArticulosAsync(string consulta, int limite, CancellationToken ct)
    {
        IReadOnlyList<ArticuloEncontrado> encontrados = RepositorioDatosPrueba.Buscar(
                repositorio.Datos.Articulos, consulta, a => $"{a.Titulo} {a.Contenido} {string.Join(' ', a.Sistemas)}", limite)
            .Select(x => new ArticuloEncontrado(x.Elemento.Id, x.Elemento.Titulo, x.Elemento.Contenido, x.Puntaje))
            .ToList();
        return Task.FromResult(encontrados);
    }

    public Task<IReadOnlyList<IncidenteServicio>> IncidentesAbiertosAsync(string sistema, int limite, CancellationToken ct)
    {
        IReadOnlyList<IncidenteServicio> incidentes = repositorio.Datos.IncidentesAbiertos
            .Where(i => i.Sistema == sistema)
            .OrderByDescending(i => i.Desde)
            .Take(limite)
            .Select(i => new IncidenteServicio(i.Id, i.Titulo, i.Estado, i.Desde))
            .ToList();
        return Task.FromResult(incidentes);
    }
}
