using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Infraestructura.Llm;

/// <summary>Valida al arrancar las reglas entre campos y devuelve cada error con su mensaje exacto.</summary>
public sealed class ValidadorOpcionesLlm : IValidateOptions<OpcionesLlm>
{
    public ValidateOptionsResult Validate(string? name, OpcionesLlm options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errores = options.ErroresDeCoherencia().ToList();
        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}

/// <summary>
/// Configuración del proveedor (ADR-006). El proveedor y el modelo se eligen aquí, nunca en el código.
/// La API key llega como secreto (dotnet user-secrets en desarrollo, archivo en /run/secrets en contenedor).
/// </summary>
public sealed class OpcionesLlm
{
    public const string Seccion = "Llm";
    public const string ProveedorAnthropic = "anthropic";
    public const string ProveedorCompatibleOpenAi = "openai_compatible";

    public static readonly IReadOnlySet<string> Proveedores =
        new HashSet<string>(StringComparer.Ordinal) { ProveedorAnthropic, ProveedorCompatibleOpenAi };

    public static readonly IReadOnlySet<string> EsfuerzosValidos =
        new HashSet<string>(StringComparer.Ordinal) { "low", "medium", "high", "xhigh", "max" };

    /// <summary>anthropic (API de mensajes) u openai_compatible (Qwen en Alibaba Model Studio u OpenRouter, Ollama…).</summary>
    [Required]
    public string Proveedor { get; set; } = ProveedorAnthropic;

    [Required]
    public Uri UrlBase { get; set; } = new("https://api.anthropic.com/");

    /// <summary>Obligatoria salvo para un servidor local (Ollama en localhost).</summary>
    public string ApiKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Falta Llm:Modelo en la configuración.")]
    public string Modelo { get; set; } = string.Empty;

    /// <summary>Solo Anthropic.</summary>
    [Required]
    public string VersionApi { get; set; } = "2026-10-21";

    /// <summary>Solo Anthropic.</summary>
    [Required]
    public string Esfuerzo { get; set; } = "medium";

    [Range(256, 64_000)]
    public int MaxTokensRespuesta { get; set; } = 16_000;

    /// <summary>Solo Anthropic: si el modelo rechaza por sus filtros, la API reintenta en otro modelo dentro de la misma llamada.</summary>
    public bool UsarFallbackServidor { get; set; } = true;

    /// <summary>
    /// Solo openai_compatible: pedir response_format con el esquema. Muchos proveedores no lo combinan con
    /// herramientas, así que viene apagado y el esquema va en las instrucciones (la validación propia no cambia).
    /// </summary>
    public bool UsarSalidaEstructurada { get; set; }

    public bool EsLocal => UrlBase.IsLoopback;

    /// <summary>Reglas que dependen de varios campos; DataAnnotations no las expresa.</summary>
    public IEnumerable<string> ErroresDeCoherencia()
    {
        if (!Proveedores.Contains(Proveedor)) yield return "Llm:Proveedor debe ser 'anthropic' u 'openai_compatible'.";
        if (!EsfuerzosValidos.Contains(Esfuerzo)) yield return "Llm:Esfuerzo debe ser low, medium, high, xhigh o max.";
        if (string.IsNullOrWhiteSpace(ApiKey) && !EsLocal)
        {
            yield return "Falta Llm:ApiKey. En desarrollo: dotnet user-secrets set \"Llm:ApiKey\" \"<tu key>\" --project src/Centinela.Agente";
        }

        // Una API key nunca viaja por HTTP sin cifrar: solo se acepta http hacia un servidor local.
        if (UrlBase.Scheme != Uri.UriSchemeHttps && !EsLocal) yield return "Llm:UrlBase debe usar https (http solo para localhost).";
    }
}
