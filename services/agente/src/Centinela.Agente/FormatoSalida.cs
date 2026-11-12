using System.Text.Json;
using Centinela.Agente.Dominio;

namespace Centinela.Agente;

/// <summary>Entrada del archivo de ticket: los mismos campos que TicketCreado (docs/EVENTOS.md).</summary>
public sealed record EntradaTicket(Guid TicketId, string Tipo, string Titulo, string Descripcion, string? SistemaReportado, string Canal)
{
    public Ticket ATicket() => new(TicketId, Tipo ?? string.Empty, Titulo ?? string.Empty, Descripcion ?? string.Empty, SistemaReportado, Canal ?? string.Empty);
}

/// <summary>Traduce el resultado al contrato TicketTriado v1 (docs/EVENTOS.md).</summary>
public static class FormatoSalida
{
    public static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public static readonly JsonSerializerOptions OpcionesIndentadas = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Estado(EstadoAnalisis estado) => estado switch
    {
        EstadoAnalisis.Completado => "completado",
        EstadoAnalisis.Invalido => "invalido",
        EstadoAnalisis.PresupuestoAgotado => "presupuesto_agotado",
        EstadoAnalisis.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, null),
    };

    public static object TicketTriado(ResultadoTriaje resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var s = resultado.Sugerencia;
        return new
        {
            ticketId = resultado.TicketId,
            estadoAnalisis = Estado(resultado.Estado),
            categoria = s?.Categoria,
            sistemaAfectado = s?.SistemaAfectado,
            prioridadSugerida = s?.PrioridadSugerida,
            justificacion = s?.Justificacion,
            duplicadosPosibles = s?.DuplicadosPosibles ?? [],
            articulosSugeridos = s?.ArticulosSugeridos ?? [],
            borradorRespuesta = s?.BorradorRespuesta,
            metricas = new
            {
                modelo = resultado.Metricas.Modelo,
                tokensEntrada = resultado.Metricas.TokensEntrada,
                tokensSalida = resultado.Metricas.TokensSalida,
                iteraciones = resultado.Metricas.Iteraciones,
                duracionMs = resultado.Metricas.DuracionMs,
            },
        };
    }
}
