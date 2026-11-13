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

    /// <summary>Si el proveedor pide esperar más que esto antes de reintentar (cuota agotada), no se reintenta.</summary>
    [Range(1, 600)]
    public int MaxEsperaReintentoSegundos { get; set; } = 30;

    /// <summary>A16: llamadas a herramientas que se aceptan en una sola respuesta del modelo.</summary>
    [Range(1, 20)]
    public int MaxHerramientasPorRespuesta { get; set; } = 5;

    /// <summary>A16: llamadas a herramientas en todo el triaje de un ticket.</summary>
    [Range(1, 50)]
    public int MaxHerramientasPorTicket { get; set; } = 12;

    /// <summary>A15: dominios que pueden aparecer como enlace en la justificación o el borrador. Vacío = ningún enlace.</summary>
    public List<string> DominiosPermitidos { get; set; } = [];

    /// <summary>
    /// Tokens (entrada + salida) que el agente puede gastar por día UTC. Al alcanzarlo, los tickets pasan a triaje
    /// manual sin llamar al modelo. La alerta de costo en dinero la calcula reportes, que conoce los precios.
    /// </summary>
    [Range(1, 1_000_000_000)]
    public long TopeDiarioTokens { get; set; } = 500_000;
}
