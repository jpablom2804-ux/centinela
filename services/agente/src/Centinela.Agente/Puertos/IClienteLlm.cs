using System.Text.Json;
using System.Text.Json.Nodes;

namespace Centinela.Agente.Puertos;

/// <summary>
/// Frontera con el proveedor del modelo. Es una interfaz porque es una API externa real
/// y porque los tests del ciclo usan un cliente falso que no gasta tokens.
/// </summary>
public interface IClienteLlm
{
    Task<RespuestaLlm> EnviarAsync(SolicitudLlm solicitud, CancellationToken ct);
}

public sealed record DefinicionHerramienta(string Nombre, string Descripcion, JsonObject EsquemaEntrada);

public sealed record SolicitudLlm(
    string Instrucciones,
    IReadOnlyList<DefinicionHerramienta> Herramientas,
    JsonObject EsquemaSalida,
    IReadOnlyList<MensajeConversacion> Mensajes);

public abstract record MensajeConversacion;

/// <summary>Texto que armamos nosotros; puede contener datos no confiables delimitados.</summary>
public sealed record MensajeUsuario(string Texto) : MensajeConversacion;

/// <summary>
/// Respuesta del modelo tal cual la devolvió el proveedor. El ciclo no la interpreta:
/// solo la reenvía sin cambios, como exige la API (bloques de razonamiento incluidos).
/// </summary>
public sealed record MensajeAsistente(object ContenidoProveedor) : MensajeConversacion;

public sealed record ResultadosHerramientas(IReadOnlyList<ResultadoHerramienta> Resultados) : MensajeConversacion;

public sealed record ResultadoHerramienta(string IdLlamada, string Contenido, bool EsError);

public sealed record LlamadaHerramienta(string Id, string Nombre, JsonElement Entrada);

public enum MotivoParada
{
    Final,
    UsoHerramientas,
    LimiteTokens,
    Rechazo,
    Otro,
}

public sealed record UsoTokens(int Entrada, int Salida);

public sealed record RespuestaLlm(
    MotivoParada Motivo,
    string Texto,
    IReadOnlyList<LlamadaHerramienta> Llamadas,
    UsoTokens Uso,
    string Modelo,
    object ContenidoProveedor,
    string? DetalleRechazo = null);

public enum TipoErrorLlm
{
    SaldoInsuficiente,
    Autenticacion,
    Permiso,
    SolicitudInvalida,
    LimiteTasa,
    Saturado,
    ErrorServidor,
    Red,
    Timeout,
}

/// <summary>Error de la API del modelo ya clasificado: el ciclo decide si reintentar según <see cref="Reintentable"/>.</summary>
public sealed class ErrorLlmException : Exception
{
    public ErrorLlmException(TipoErrorLlm tipo, bool reintentable, int? codigoHttp = null, TimeSpan? esperaSugerida = null, Exception? causa = null)
        : base($"Error de la API del modelo: {tipo} (HTTP {codigoHttp?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"})", causa)
    {
        Tipo = tipo;
        Reintentable = reintentable;
        CodigoHttp = codigoHttp;
        EsperaSugerida = esperaSugerida;
    }

    public ErrorLlmException()
        : this(TipoErrorLlm.ErrorServidor, reintentable: false)
    {
    }

    public ErrorLlmException(string message)
        : base(message)
    {
    }

    public ErrorLlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TipoErrorLlm Tipo { get; }
    public bool Reintentable { get; }
    public int? CodigoHttp { get; }
    public TimeSpan? EsperaSugerida { get; }

    /// <summary>Errores que ningún reintento arregla y que alguien tiene que atender (saldo, credenciales).</summary>
    public bool RequiereAlerta => Tipo is TipoErrorLlm.SaldoInsuficiente or TipoErrorLlm.Autenticacion or TipoErrorLlm.Permiso;
}
