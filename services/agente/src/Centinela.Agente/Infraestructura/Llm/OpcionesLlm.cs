using System.ComponentModel.DataAnnotations;

namespace Centinela.Agente.Infraestructura.Llm;

/// <summary>
/// Configuración del proveedor. El modelo se elige aquí, nunca en el código. La API key llega
/// como secreto (dotnet user-secrets en desarrollo, archivo en /run/secrets en contenedor).
/// </summary>
public sealed class OpcionesLlm
{
    public const string Seccion = "Llm";

    public static readonly IReadOnlySet<string> EsfuerzosValidos =
        new HashSet<string>(StringComparer.Ordinal) { "low", "medium", "high", "xhigh", "max" };

    [Required]
    public Uri UrlBase { get; set; } = new("https://api.anthropic.com/");

    [Required(ErrorMessage = "Falta Llm:ApiKey. En desarrollo: dotnet user-secrets set \"Llm:ApiKey\" \"<tu key>\" --project src/Centinela.Agente")]
    public string ApiKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Falta Llm:Modelo en la configuración.")]
    public string Modelo { get; set; } = string.Empty;

    [Required]
    public string VersionApi { get; set; } = "2026-10-21";

    [Required]
    public string Esfuerzo { get; set; } = "medium";

    [Range(256, 64_000)]
    public int MaxTokensRespuesta { get; set; } = 16_000;

    /// <summary>Si el modelo rechaza por sus filtros, la API reintenta en otro modelo dentro de la misma llamada.</summary>
    public bool UsarFallbackServidor { get; set; } = true;
}
