using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Infraestructura.ApiInterna;

/// <summary>
/// Cliente HTTP de la API interna de mesa-servicio: solo GET, con la cuenta de servicio del agente. Cualquier falla
/// (caída, timeout, 4xx/5xx, respuesta ilegible) se reporta como <see cref="ConsultaNoDisponibleException"/> y la
/// herramienta le dice al modelo que siga sin ese dato. Lo que llega ya viene enmascarado, y el ciclo lo vuelve a
/// verificar antes de pasarlo al modelo.
/// </summary>
public sealed class ClienteApiInterna : IConsultasMesaServicio
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public ClienteApiInterna(HttpClient http, IOptions<OpcionesApiInterna> opciones)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(opciones);
        var o = opciones.Value;
        _http = http;
        _http.BaseAddress = o.UrlBase;
        _http.Timeout = TimeSpan.FromSeconds(o.TimeoutSegundos);
        var credencial = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Usuario}:{o.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credencial);
    }

    private sealed record TicketDto(string Id, string Titulo, string? Estado, string? Sistema, string? Solucion, double Similitud);

    private sealed record ArticuloDto(string Id, string Titulo, string Extracto, double Similitud);

    private sealed record IncidenteDto(string Id, string Titulo, string Estado, DateTimeOffset Desde);

    // [FLUJO HERRAMIENTAS_AGENTE 1] GET a la API interna con la cuenta de servicio → mesa-servicio (paso 2a/2b/2c)
    public async Task<IReadOnlyList<TicketSimilar>> BuscarTicketsSimilaresAsync(Guid ticketEnCurso, string consulta, int limite, CancellationToken ct)
    {
        var tickets = await ObtenerAsync<TicketDto>(
            $"interna/tickets/{ticketEnCurso}/similares?consulta={Uri.EscapeDataString(consulta)}&limite={Limite(limite)}", ct);
        return tickets.Select(t => new TicketSimilar(t.Id, t.Titulo, t.Estado, t.Sistema, null, t.Solucion, t.Similitud)).ToList();
    }

    public async Task<IReadOnlyList<ArticuloEncontrado>> BuscarArticulosAsync(string consulta, int limite, CancellationToken ct)
    {
        var articulos = await ObtenerAsync<ArticuloDto>(
            $"interna/articulos?consulta={Uri.EscapeDataString(consulta)}&limite={Limite(limite)}", ct);
        return articulos.Select(a => new ArticuloEncontrado(a.Id, a.Titulo, a.Extracto, a.Similitud)).ToList();
    }

    public async Task<IReadOnlyList<IncidenteServicio>> IncidentesAbiertosAsync(string sistema, int limite, CancellationToken ct)
    {
        var incidentes = await ObtenerAsync<IncidenteDto>(
            $"interna/sistemas/{Uri.EscapeDataString(sistema)}/incidentes?limite={Limite(limite)}", ct);
        return incidentes.Select(i => new IncidenteServicio(i.Id, i.Titulo, i.Estado, i.Desde)).ToList();
    }

    private static string Limite(int limite) => Math.Clamp(limite, 1, 10).ToString(CultureInfo.InvariantCulture);

    private async Task<List<T>> ObtenerAsync<T>(string ruta, CancellationToken ct)
    {
        try
        {
            using var respuesta = await _http.GetAsync(new Uri(ruta, UriKind.Relative), ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new ConsultaNoDisponibleException($"API interna respondió {(int)respuesta.StatusCode}");
            }

            return await respuesta.Content.ReadFromJsonAsync<List<T>>(Opciones, ct)
                   ?? throw new ConsultaNoDisponibleException("API interna devolvió un cuerpo vacío");
        }
        catch (HttpRequestException e)
        {
            throw new ConsultaNoDisponibleException("API interna no disponible", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new ConsultaNoDisponibleException("API interna no respondió a tiempo", e);
        }
        catch (JsonException e)
        {
            throw new ConsultaNoDisponibleException("API interna devolvió algo ilegible", e);
        }
    }
}
