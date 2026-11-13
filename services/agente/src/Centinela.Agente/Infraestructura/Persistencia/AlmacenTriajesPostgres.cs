using Centinela.Agente.Aplicacion;
using Centinela.Agente.Puertos;
using Npgsql;

namespace Centinela.Agente.Infraestructura.Persistencia;

/// <summary>
/// El bloqueo por ticket es un advisory lock de sesión de PostgreSQL sobre una conexión dedicada: si la réplica
/// cae, la conexión se corta y el bloqueo se libera solo, sin tiempos de expiración que ajustar.
/// </summary>
public sealed class AlmacenTriajesPostgres(NpgsqlDataSource fuente) : IAlmacenTriajes
{
    public async Task<ISesionTicket?> TomarTicketAsync(Guid ticketId, CancellationToken ct)
    {
        var conexion = await fuente.OpenConnectionAsync(ct);
        try
        {
            await using var comando = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@t::text, 0))", conexion);
            comando.Parameters.AddWithValue("t", ticketId);
            if ((bool)(await comando.ExecuteScalarAsync(ct))!)
            {
                return new Sesion(conexion, ticketId);
            }
        }
        catch
        {
            await conexion.DisposeAsync();
            throw;
        }

        await conexion.DisposeAsync();
        return null;
    }

    private sealed class Sesion(NpgsqlConnection conexion, Guid ticketId) : ISesionTicket
    {
        public async Task<bool> MensajeYaProcesadoAsync(Guid mensajeId, CancellationToken ct)
        {
            await using var comando = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM agente.mensaje_procesado WHERE id = @id)", conexion);
            comando.Parameters.AddWithValue("id", mensajeId);
            return (bool)(await comando.ExecuteScalarAsync(ct))!;
        }

        public async Task<TriajeGuardado?> TriajeGuardadoAsync(CancellationToken ct)
        {
            await using var comando = new NpgsqlCommand("""
                SELECT evento_id, correlation_id, estado, modelo, tokens_entrada, tokens_salida, cuerpo, creado_en
                  FROM agente.triaje WHERE ticket_id = @t
                """, conexion);
            comando.Parameters.AddWithValue("t", ticketId);
            await using var lector = await comando.ExecuteReaderAsync(ct);
            if (!await lector.ReadAsync(ct)) return null;
            return new TriajeGuardado(lector.GetGuid(0), ticketId, lector.GetString(1), lector.GetString(2), lector.GetString(3),
                lector.GetInt32(4), lector.GetInt32(5), lector.GetString(6), lector.GetFieldValue<DateTimeOffset>(7));
        }

        public async Task GuardarTriajeAsync(TriajeGuardado triaje, CancellationToken ct)
        {
            await using var comando = new NpgsqlCommand("""
                INSERT INTO agente.triaje (ticket_id, evento_id, correlation_id, estado, modelo, tokens_entrada, tokens_salida, cuerpo, creado_en)
                VALUES (@t, @e, @c, @s, @m, @te, @ts, @cuerpo, @creado)
                """, conexion);
            comando.Parameters.AddWithValue("t", triaje.TicketId);
            comando.Parameters.AddWithValue("e", triaje.EventoId);
            comando.Parameters.AddWithValue("c", triaje.CorrelationId);
            comando.Parameters.AddWithValue("s", triaje.Estado);
            comando.Parameters.AddWithValue("m", triaje.Modelo);
            comando.Parameters.AddWithValue("te", triaje.TokensEntrada);
            comando.Parameters.AddWithValue("ts", triaje.TokensSalida);
            comando.Parameters.AddWithValue("cuerpo", triaje.Cuerpo);
            comando.Parameters.AddWithValue("creado", triaje.CreadoEn);
            await comando.ExecuteNonQueryAsync(ct);
        }

        public async Task MarcarMensajeProcesadoAsync(Guid mensajeId, CancellationToken ct)
        {
            await using var comando = new NpgsqlCommand("""
                INSERT INTO agente.mensaje_procesado (id, ticket_id, procesado_en) VALUES (@id, @t, now())
                ON CONFLICT (id) DO NOTHING
                """, conexion);
            comando.Parameters.AddWithValue("id", mensajeId);
            comando.Parameters.AddWithValue("t", ticketId);
            await comando.ExecuteNonQueryAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            // El bloqueo es de sesión y la conexión vuelve al pool: hay que soltarlo antes de devolverla.
            try
            {
                if (conexion.State == System.Data.ConnectionState.Open)
                {
                    await using var comando = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@t::text, 0))", conexion);
                    comando.Parameters.AddWithValue("t", ticketId);
                    await comando.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await conexion.DisposeAsync();
            }
        }
    }
}
