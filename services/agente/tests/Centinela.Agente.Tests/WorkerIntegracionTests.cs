using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Centinela.Agente.Infraestructura;
using Centinela.Agente.Puertos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Centinela.Agente.Tests;

/// <summary>
/// PostgreSQL y RabbitMQ reales, con el mismo script de init y las mismas definiciones del proyecto (A3): el
/// agente se conecta con su propio usuario y solo puede leer su cola y escribir en "triajes".
/// </summary>
public sealed class EntornoWorker : IAsyncLifetime
{
    public const string ClaveAdminMq = "clave-mq-admin-prueba";
    public const string ClaveAgenteMq = "clave-mq-agente-prueba";
    public const string ClaveAgenteBd = "clave-agente-prueba";

    private static readonly string Raiz = BuscarRaiz();

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("docker.io/pgvector/pgvector:pg18")
        .WithDatabase("centinela")
        .WithUsername("centinela_admin")
        .WithPassword("clave-admin-prueba")
        .WithResourceMapping(File.ReadAllBytes(Path.Combine(Raiz, "deploy", "postgres", "init", "01-esquemas-y-usuarios.sh")),
            "/docker-entrypoint-initdb.d/01-esquemas-y-usuarios.sh", Convert.ToUInt32("755", 8))
        .WithResourceMapping(Encoding.UTF8.GetBytes("clave-dueno-prueba"), "/run/secrets/mesa_servicio_db_password")
        .WithResourceMapping(Encoding.UTF8.GetBytes("clave-app-prueba"), "/run/secrets/mesa_servicio_app_db_password")
        .WithResourceMapping(Encoding.UTF8.GetBytes(ClaveAgenteBd), "/run/secrets/agente_db_password")
        .WithResourceMapping(Encoding.UTF8.GetBytes("clave-powerbi-prueba"), "/run/secrets/powerbi_ro_db_password")
        .Build();

    public RabbitMqContainer Rabbit { get; } = new RabbitMqBuilder("docker.io/library/rabbitmq:4-management")
        .WithResourceMapping(Encoding.UTF8.GetBytes(Definiciones()), "/etc/rabbitmq/definiciones.json")
        .WithResourceMapping(new FileInfo(Path.Combine(Raiz, "deploy", "rabbitmq", "20-centinela.conf")), "/etc/rabbitmq/conf.d/")
        .Build();

    public async Task InitializeAsync() => await Task.WhenAll(Postgres.StartAsync(), Rabbit.StartAsync());

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Rabbit.DisposeAsync();
    }

    public int PuertoRabbit => Rabbit.GetMappedPublicPort(5672);

    public async Task<IConnection> ConectarComoAdminAsync() => await new ConnectionFactory
    {
        HostName = Rabbit.Hostname,
        Port = PuertoRabbit,
        VirtualHost = "centinela",
        UserName = "centinela",
        Password = ClaveAdminMq,
    }.CreateConnectionAsync();

    /// <summary>Un agente completo (worker) con el modelo falso; la configuración es la misma que fuera de las pruebas.</summary>
    public IHost CrearAgente(ClienteLlmFalso llm, IDictionary<string, string?>? extra = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:UrlBase"] = "http://localhost:9/",
            ["Llm:Modelo"] = "modelo-falso",
            ["Agente:BackoffBaseMs"] = "1",
            ["Mensajeria:Host"] = Rabbit.Hostname,
            ["Mensajeria:Puerto"] = PuertoRabbit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mensajeria:BackoffBaseMs"] = "10",
            ["rabbitmq_agente_password"] = ClaveAgenteMq,
            ["BaseDatos:Host"] = Postgres.Hostname,
            ["BaseDatos:Puerto"] = Postgres.GetMappedPublicPort(5432).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["agente_db_password"] = ClaveAgenteBd,
            ["api_interna_agente_password"] = "clave-api-interna-prueba",
        });
        if (extra is not null) builder.Configuration.AddInMemoryCollection(extra);
        builder.Services.AgregarAgente(builder.Configuration, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Herramientas", "datos-prueba.json"));
        builder.Services.AgregarWorker(builder.Configuration);
        builder.Services.AddSingleton<IClienteLlm>(llm);
        return builder.Build();
    }

    private static string Definiciones() =>
        File.ReadAllText(Path.Combine(Raiz, "deploy", "rabbitmq", "definiciones.plantilla.json"))
            .Replace("__HASH_ADMIN__", HashRabbit(ClaveAdminMq), StringComparison.Ordinal)
            .Replace("__HASH_MESA_SERVICIO__", HashRabbit("clave-mq-mesa-prueba"), StringComparison.Ordinal)
            .Replace("__HASH_AGENTE__", HashRabbit(ClaveAgenteMq), StringComparison.Ordinal);

    /// <summary>rabbit_password_hashing_sha256: base64(sal de 4 bytes + SHA-256(sal + clave)), como scripts/secretos.sh.</summary>
    private static string HashRabbit(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(4);
        var resumen = SHA256.HashData([.. sal, .. Encoding.UTF8.GetBytes(clave)]);
        return Convert.ToBase64String([.. sal, .. resumen]);
    }

    private static string BuscarRaiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "deploy", "rabbitmq"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio");
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionWorker : ICollectionFixture<EntornoWorker>
{
    public const string Nombre = "worker";
}

[Collection(ColeccionWorker.Nombre)]
public sealed class WorkerIntegracionTests(EntornoWorker entorno) : IAsyncLifetime
{
    private const string ColaEntrada = "agente.tickets-creados";
    private const string ColaSalida = "mesa-servicio.tickets-triados";
    private const string Dlq = "agente.tickets-creados.dlq";
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);

    private IConnection _admin = null!;
    private IChannel _canal = null!;

    public async Task InitializeAsync()
    {
        _admin = await entorno.ConectarComoAdminAsync();
        _canal = await _admin.CreateChannelAsync();
        foreach (var cola in new[] { ColaEntrada, ColaSalida, Dlq }) await _canal.QueuePurgeAsync(cola);
    }

    public async Task DisposeAsync()
    {
        await _canal.DisposeAsync();
        await _admin.DisposeAsync();
    }

    private static ClienteLlmFalso ModeloQueTria() =>
        new ClienteLlmFalso().RespondeSiempre(_ => Respuestas.Final(Respuestas.TriajeVpn()));

    private static string TicketCreado(Guid mensajeId, Guid ticketId, string correlationId = "a1b2c3d4e5f60718293a4b5c6d7e8f90") =>
        JsonSerializer.Serialize(new
        {
            id = mensajeId,
            tipo = "TicketCreado",
            version = 1,
            correlationId,
            ocurridoEn = "2026-11-12T15:00:00.000Z",
            datos = new
            {
                ticketId,
                tipo = "incidente",
                titulo = "VPN no conecta desde casa",
                descripcion = "Desde ayer no puedo conectar la VPN; se queda en conectando.",
                sistemaReportado = "vpn",
                canal = "portal",
            },
        });

    private async Task PublicarAsync(string cuerpo) =>
        await _canal.BasicPublishAsync("tickets", "ticket.creado", Encoding.UTF8.GetBytes(cuerpo));

    /// <summary>Lee los TicketTriado que lleguen durante la espera (o hasta tener los esperados).</summary>
    private async Task<List<(BasicGetResult Entrega, JsonElement Sobre)>> RecibirAsync(string cola, int esperados, TimeSpan? espera = null)
    {
        var recibidos = new List<(BasicGetResult, JsonElement)>();
        var limite = DateTime.UtcNow + (espera ?? Espera);
        while (recibidos.Count < esperados && DateTime.UtcNow < limite)
        {
            var entrega = await _canal.BasicGetAsync(cola, autoAck: true);
            if (entrega is null)
            {
                await Task.Delay(100);
                continue;
            }

            recibidos.Add((entrega, ComoJson(entrega.Body)));
        }

        return recibidos;
    }

    /// <summary>En la DLQ puede haber cualquier cosa (justamente lo que no cumplía el contrato).</summary>
    private static JsonElement ComoJson(ReadOnlyMemory<byte> cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            return documento.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static async Task<IHost> ArrancarAsync(IHost agente)
    {
        await agente.StartAsync();
        return agente;
    }

    [Fact]
    public async Task TriaUnTicketDePuntaAPunta()
    {
        var llm = ModeloQueTria();
        using var agente = await ArrancarAsync(entorno.CrearAgente(llm));
        var ticketId = Guid.NewGuid();

        await PublicarAsync(TicketCreado(Guid.NewGuid(), ticketId));
        var recibidos = await RecibirAsync(ColaSalida, 1);
        await agente.StopAsync();

        var (entrega, sobre) = Assert.Single(recibidos);
        Assert.Equal("TicketTriado", sobre.GetProperty("tipo").GetString());
        Assert.Equal(1, sobre.GetProperty("version").GetInt32());
        Assert.Equal("a1b2c3d4e5f60718293a4b5c6d7e8f90", sobre.GetProperty("correlationId").GetString());
        Assert.Equal(ticketId, sobre.GetProperty("datos").GetProperty("ticketId").GetGuid());
        Assert.Equal("completado", sobre.GetProperty("datos").GetProperty("estadoAnalisis").GetString());
        Assert.Equal(sobre.GetProperty("id").GetString(), entrega.BasicProperties.MessageId);
        Assert.Equal("a1b2c3d4e5f60718293a4b5c6d7e8f90", entrega.BasicProperties.CorrelationId);
        Assert.Single(llm.Solicitudes);
    }

    [Fact]
    public async Task UnMensajeRepetidoNoVuelveAlModelo()
    {
        var llm = ModeloQueTria();
        using var agente = await ArrancarAsync(entorno.CrearAgente(llm));
        var mensaje = TicketCreado(Guid.NewGuid(), Guid.NewGuid());

        await PublicarAsync(mensaje);
        await PublicarAsync(mensaje);
        var recibidos = await RecibirAsync(ColaSalida, 2, TimeSpan.FromSeconds(8));
        await agente.StopAsync();

        Assert.Single(recibidos);
        Assert.Single(llm.Solicitudes);
    }

    [Fact]
    public async Task ElMismoTicketEnOtroMensajeRepublicaElResultadoGuardado()
    {
        // Por ejemplo, el outbox publicó dos veces: se paga una sola vez y mesa-servicio recibe el mismo evento (mismo id).
        var llm = ModeloQueTria();
        using var agente = await ArrancarAsync(entorno.CrearAgente(llm));
        var ticketId = Guid.NewGuid();

        await PublicarAsync(TicketCreado(Guid.NewGuid(), ticketId));
        await PublicarAsync(TicketCreado(Guid.NewGuid(), ticketId));
        var recibidos = await RecibirAsync(ColaSalida, 2);
        await agente.StopAsync();

        Assert.Equal(2, recibidos.Count);
        Assert.Equal(recibidos[0].Sobre.GetProperty("id").GetString(), recibidos[1].Sobre.GetProperty("id").GetString());
        Assert.Single(llm.Solicitudes);
    }

    [Theory]
    [InlineData("esto no es JSON")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000001","tipo":"OtroEvento","version":1,"correlationId":"a1b2c3d4e5f6","datos":{}}""")]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000002","tipo":"TicketCreado","version":2,"correlationId":"a1b2c3d4e5f6","datos":{}}""")]
    public async Task UnMensajeQueNoCumpleElContratoVaALaDlqSinLlamarAlModelo(string cuerpo)
    {
        var llm = ModeloQueTria();
        using var agente = await ArrancarAsync(entorno.CrearAgente(llm));

        await PublicarAsync(cuerpo);
        var muertos = await RecibirAsync(Dlq, 1);
        await agente.StopAsync();

        Assert.Single(muertos);
        Assert.Empty(llm.Solicitudes);
    }

    [Fact]
    public async Task TumbarElAgenteAMitadNoPierdeElTicket()
    {
        // El primer agente se queda "pensando" y se apaga a mitad: sin ack, el broker entrega el mensaje al segundo.
        var lento = ModeloQueTria();
        lento.Demora = TimeSpan.FromMinutes(5);
        var ticketId = Guid.NewGuid();
        using (var primero = await ArrancarAsync(entorno.CrearAgente(lento)))
        {
            await PublicarAsync(TicketCreado(Guid.NewGuid(), ticketId));
            var limite = DateTime.UtcNow + Espera;
            while (lento.Solicitudes.Count == 0 && DateTime.UtcNow < limite) await Task.Delay(100);
            Assert.Single(lento.Solicitudes);
            await primero.StopAsync(TimeSpan.FromSeconds(10));
        }

        var rapido = ModeloQueTria();
        using var segundo = await ArrancarAsync(entorno.CrearAgente(rapido));
        var recibidos = await RecibirAsync(ColaSalida, 1);
        await segundo.StopAsync();

        var (_, sobre) = Assert.Single(recibidos);
        Assert.Equal(ticketId, sobre.GetProperty("datos").GetProperty("ticketId").GetGuid());
        Assert.Single(rapido.Solicitudes);
    }

    [Fact]
    public async Task ConElTopeDiarioAlcanzadoNoLlamaAlModeloYElTicketVaATriajeManual()
    {
        var llm = ModeloQueTria();
        using var agente = await ArrancarAsync(entorno.CrearAgente(llm, new Dictionary<string, string?> { ["Agente:TopeDiarioTokens"] = "1" }));
        var primero = Guid.NewGuid();
        var segundo = Guid.NewGuid();

        // El primero puede triarse si nada se gastó hoy; con eso el tope (1 token) queda superado para el segundo.
        await PublicarAsync(TicketCreado(Guid.NewGuid(), primero));
        Assert.NotEmpty(await RecibirAsync(ColaSalida, 1));
        await PublicarAsync(TicketCreado(Guid.NewGuid(), segundo));
        var recibidos = await RecibirAsync(ColaSalida, 1);
        await agente.StopAsync();

        var datos = Assert.Single(recibidos).Sobre.GetProperty("datos");
        Assert.Equal(segundo, datos.GetProperty("ticketId").GetGuid());
        Assert.Equal("presupuesto_agotado", datos.GetProperty("estadoAnalisis").GetString());
        Assert.Equal(JsonValueKind.Null, datos.GetProperty("prioridadSugerida").ValueKind);
        Assert.True(llm.Solicitudes.Count <= 1);
    }

    [Fact]
    public async Task ElUsuarioDelAgenteNoPuedePublicarComoMesaServicio()
    {
        // A3: con su usuario, el agente no puede inyectar un TicketCreado falso ni leer los triajes.
        await using var conexion = await new ConnectionFactory
        {
            HostName = entorno.Rabbit.Hostname,
            Port = entorno.PuertoRabbit,
            VirtualHost = "centinela",
            UserName = "agente",
            Password = EntornoWorker.ClaveAgenteMq,
        }.CreateConnectionAsync();

        await using var canal = await conexion.CreateChannelAsync();
        var lectura = await Record.ExceptionAsync(() => canal.BasicGetAsync(ColaSalida, autoAck: true));
        Assert.Contains("ACCESS_REFUSED", lectura?.ToString() ?? string.Empty, StringComparison.Ordinal);

        await using var otroCanal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true));
        var publicacion = await Record.ExceptionAsync(() =>
            otroCanal.BasicPublishAsync("tickets", "ticket.creado", true, new BasicProperties(), Encoding.UTF8.GetBytes("{}")).AsTask());
        Assert.Contains("ACCESS_REFUSED", publicacion?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }
}
