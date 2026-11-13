using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Centinela.Agente.Infraestructura.Persistencia;

/// <summary>
/// Aplica las migraciones SQL embebidas (V1__*.sql, V2__*.sql…) antes de que arranque el consumidor. Varias
/// réplicas pueden arrancar a la vez: un bloqueo en la base hace que una sola migre y las demás esperen.
/// Una migración aplicada nunca se edita: los cambios van en una versión nueva.
/// </summary>
public sealed partial class MigradorEsquema(NpgsqlDataSource fuente, ILogger<MigradorEsquema> log) : IHostedService
{
    private const long ClaveBloqueo = 7_302_141_002L;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var conexion = await fuente.OpenConnectionAsync(cancellationToken);
        await using var transaccion = await conexion.BeginTransactionAsync(cancellationToken);
        await Ejecutar(conexion, "SELECT pg_advisory_xact_lock(@clave)", cancellationToken, ("clave", ClaveBloqueo));
        await Ejecutar(conexion, """
            CREATE TABLE IF NOT EXISTS agente.migracion (
                version     integer      PRIMARY KEY,
                nombre      varchar(200) NOT NULL,
                aplicada_en timestamptz  NOT NULL DEFAULT now()
            )
            """, cancellationToken);

        foreach (var (version, nombre, sql) in Migraciones())
        {
            await using var existe = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM agente.migracion WHERE version = @v)", conexion);
            existe.Parameters.AddWithValue("v", version);
            if ((bool)(await existe.ExecuteScalarAsync(cancellationToken))!) continue;

            await Ejecutar(conexion, sql, cancellationToken);
            await Ejecutar(conexion, "INSERT INTO agente.migracion (version, nombre) VALUES (@v, @n)", cancellationToken,
                ("v", version), ("n", nombre));
            MigracionAplicada(log, version, nombre);
        }

        await transaccion.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task Ejecutar(NpgsqlConnection conexion, string sql, CancellationToken ct, params (string Nombre, object Valor)[] parametros)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        foreach (var (nombre, valor) in parametros) comando.Parameters.AddWithValue(nombre, valor);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static List<(int Version, string Nombre, string Sql)> Migraciones()
    {
        var ensamblado = Assembly.GetExecutingAssembly();
        return ensamblado.GetManifestResourceNames()
            .Select(r => (Recurso: r, Coincidencia: PatronMigracion().Match(r)))
            .Where(x => x.Coincidencia.Success)
            .Select(x =>
            {
                using var flujo = ensamblado.GetManifestResourceStream(x.Recurso)!;
                using var lector = new StreamReader(flujo);
                return (int.Parse(x.Coincidencia.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    x.Coincidencia.Groups[2].Value, lector.ReadToEnd());
            })
            .OrderBy(m => m.Item1)
            .ToList();
    }

    [GeneratedRegex(@"Migraciones\.V(\d+)__(.+)\.sql$")]
    private static partial Regex PatronMigracion();

    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Migración V{Version} ({Nombre}) aplicada")]
    private static partial void MigracionAplicada(ILogger logger, int version, string nombre);
}
