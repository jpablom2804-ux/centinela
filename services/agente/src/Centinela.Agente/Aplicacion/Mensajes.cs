using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Centinela.Agente.Dominio;

namespace Centinela.Agente.Aplicacion;

/// <summary>Un TicketCreado ya leído y validado (docs/EVENTOS.md). El texto del ticket sigue siendo no confiable.</summary>
public sealed record MensajeTicketCreado(Guid Id, string CorrelationId, Ticket Ticket);

/// <summary>
/// El resultado de un triaje tal como se publica (sobre TicketTriado v1). Se guarda antes de publicar: si el
/// mismo ticket vuelve a llegar, se republica este mismo evento, con el mismo id, sin llamar otra vez al modelo.
/// </summary>
public sealed record TriajeGuardado(
    Guid EventoId,
    Guid TicketId,
    string CorrelationId,
    string Estado,
    string Modelo,
    int TokensEntrada,
    int TokensSalida,
    string Cuerpo,
    DateTimeOffset CreadoEn)
{
    public const string Tipo = "TicketTriado";
    public const int Version = 1;

    // Acentos legibles en el mensaje; < > & ' siguen escapados.
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static TriajeGuardado De(ResultadoTriaje resultado, string correlationId, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var id = Guid.NewGuid();
        var sobre = new
        {
            id,
            tipo = Tipo,
            version = Version,
            correlationId,
            ocurridoEn = ahora.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            datos = FormatoSalida.TicketTriado(resultado),
        };
        var m = resultado.Metricas;
        return new TriajeGuardado(id, resultado.TicketId, correlationId, FormatoSalida.Estado(resultado.Estado), m.Modelo,
            m.TokensEntrada, m.TokensSalida, JsonSerializer.Serialize(sobre, Opciones), ahora);
    }
}

public enum ResultadoProcesamiento
{
    /// <summary>Se llamó al modelo y se publicó el resultado.</summary>
    Triado,

    /// <summary>El ticket ya estaba triado: se republicó el resultado guardado sin llamar al modelo.</summary>
    Republicado,

    /// <summary>Este mismo mensaje ya se había procesado: se descarta.</summary>
    Duplicado,

    /// <summary>Otra réplica está triando el mismo ticket ahora mismo: esta copia se descarta.</summary>
    EnCursoEnOtraReplica,
}
