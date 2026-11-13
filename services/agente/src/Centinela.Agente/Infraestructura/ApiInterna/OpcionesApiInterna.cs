using System.ComponentModel.DataAnnotations;

namespace Centinela.Agente.Infraestructura.ApiInterna;

/// <summary>
/// API interna de solo lectura de mesa-servicio (única llamada síncrona permitida entre servicios, ADR-003).
/// La contraseña de la cuenta de servicio llega como secreto en archivo (api_interna_agente_password, ADR-005).
/// </summary>
public sealed class OpcionesApiInterna
{
    public const string Seccion = "ApiInterna";
    public const string ClaveSecreto = "api_interna_agente_password";

    [Required]
    public Uri UrlBase { get; set; } = new("http://127.0.0.1:8080/");

    [Required]
    public string Usuario { get; set; } = "agente";

    [Required(ErrorMessage = "Falta el secreto api_interna_agente_password")]
    public string? Password { get; set; }

    /// <summary>Una herramienta lenta no debe comerse el presupuesto de tiempo del ticket.</summary>
    [Range(1, 60)]
    public int TimeoutSegundos { get; set; } = 5;
}
