using System.Text.Json;
using System.Text.Json.Nodes;
using Centinela.Agente.Aplicacion;
using Centinela.Agente.Dominio;
using Centinela.Agente.Infraestructura.Herramientas;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Centinela.Agente.Tests;

/// <summary>Cliente del modelo con guion: no hace llamadas reales ni gasta tokens.</summary>
public sealed class ClienteLlmFalso : IClienteLlm
{
    private readonly Queue<Func<SolicitudLlm, RespuestaLlm>> _guion = new();
    private Func<SolicitudLlm, RespuestaLlm>? _siempre;

    public List<SolicitudLlm> Solicitudes { get; } = [];

    public TimeSpan Demora { get; set; } = TimeSpan.Zero;

    public ClienteLlmFalso Responde(RespuestaLlm respuesta)
    {
        _guion.Enqueue(_ => respuesta);
        return this;
    }

    public ClienteLlmFalso Falla(ErrorLlmException error)
    {
        _guion.Enqueue(_ => throw error);
        return this;
    }

    public ClienteLlmFalso RespondeSiempre(Func<SolicitudLlm, RespuestaLlm> respuesta)
    {
        _siempre = respuesta;
        return this;
    }

    public async Task<RespuestaLlm> EnviarAsync(SolicitudLlm solicitud, CancellationToken ct)
    {
        Solicitudes.Add(solicitud);
        if (Demora > TimeSpan.Zero) await Task.Delay(Demora, ct);
        if (_guion.Count > 0) return _guion.Dequeue()(solicitud);
        return _siempre?.Invoke(solicitud) ?? throw new InvalidOperationException("Se terminó el guion del cliente falso.");
    }
}

public static class Respuestas
{
    public static RespuestaLlm Herramienta(string nombre, string entradaJson, string id = "toolu_1", int tokensEntrada = 1_000, int tokensSalida = 50)
    {
        using var documento = JsonDocument.Parse(entradaJson);
        var contenido = new JsonArray(new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = id,
            ["name"] = nombre,
            ["input"] = JsonNode.Parse(entradaJson),
        });
        return new RespuestaLlm(MotivoParada.UsoHerramientas, string.Empty,
            [new LlamadaHerramienta(id, nombre, documento.RootElement.Clone())],
            new UsoTokens(tokensEntrada, tokensSalida), "modelo-falso", contenido);
    }

    public static RespuestaLlm Final(string texto, int tokensEntrada = 1_500, int tokensSalida = 300) =>
        new(MotivoParada.Final, texto, [], new UsoTokens(tokensEntrada, tokensSalida), "modelo-falso",
            new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texto }));

    public static RespuestaLlm ConMotivo(MotivoParada motivo, string? detalle = null) =>
        new(motivo, string.Empty, [], new UsoTokens(100, 10), "modelo-falso", new JsonArray(), detalle);

    public static string TriajeVpn(string prioridad = "P3", string[]? duplicados = null, string[]? articulos = null) =>
        JsonSerializer.Serialize(new
        {
            categoria = "red_y_conectividad",
            sistemaAfectado = "vpn",
            prioridadSugerida = prioridad,
            justificacion = "Afecta a una persona que trabaja desde casa; hay una solución conocida.",
            duplicadosPosibles = duplicados ?? [],
            articulosSugeridos = articulos ?? [],
            borradorRespuesta = "Hola, recibimos tu reporte. Un técnico revisará la conexión de la VPN contigo.",
        });
}

/// <summary>Logger que guarda los mensajes para comprobar alertas y que no se registren datos del ticket.</summary>
public sealed class LoggerCapturador<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Mensaje)> Registros { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Registros.Add((logLevel, formatter(state, exception)));
    }
}

public static class Fabrica
{
    private static readonly Lazy<RepositorioDatosPrueba> Repo = new(() =>
        RepositorioDatosPrueba.Cargar(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Herramientas", "datos-prueba.json")));

    public static RepositorioDatosPrueba Repositorio => Repo.Value;

    public static IConsultasMesaServicio Consultas => new ConsultasDatosPrueba(Repo.Value);

    public static IHerramienta[] Herramientas() =>
    [
        new HerramientaBuscarTicketsSimilares(Consultas),
        new HerramientaBuscarArticulos(Consultas),
        new HerramientaConsultarEstadoServicio(Consultas),
    ];

    public static CicloAgente Ciclo(IClienteLlm cliente, OpcionesAgente? opciones = null, ILogger<CicloAgente>? logger = null) =>
        new(cliente, Herramientas(), Options.Create(opciones ?? new OpcionesAgente { BackoffBaseMs = 1 }),
            TimeProvider.System, logger ?? new LoggerCapturador<CicloAgente>());

    public static Ticket TicketVpn(string? descripcion = null) => new(
        Guid.NewGuid(),
        "incidente",
        "VPN no conecta desde casa",
        descripcion ?? "Desde ayer no puedo conectar la VPN desde casa; se queda en conectando y luego da error.",
        "vpn",
        "portal");
}
