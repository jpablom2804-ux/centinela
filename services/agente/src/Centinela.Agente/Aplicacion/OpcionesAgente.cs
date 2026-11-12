using System.ComponentModel.DataAnnotations;

namespace Centinela.Agente.Aplicacion;

/// <summary>Límites del ciclo por ticket. Vienen de configuración y se validan al arrancar.</summary>
public sealed class OpcionesAgente
{
    public const string Seccion = "Agente";

    [Range(1, 20)]
    public int MaxIteraciones { get; set; } = 6;

    [Range(1_000, 1_000_000)]
    public int MaxTokensTicket { get; set; } = 40_000;

    [Range(5, 600)]
    public int TimeoutSegundos { get; set; } = 90;

    [Range(0, 5)]
    public int MaxReintentos { get; set; } = 2;

    [Range(0, 60_000)]
    public int BackoffBaseMs { get; set; } = 1_000;

    /// <summary>Turnos de corrección si la respuesta final no pasa la validación (0 = ninguno). Cuentan en el presupuesto.</summary>
    [Range(0, 3)]
    public int MaxReparaciones { get; set; } = 1;
}
