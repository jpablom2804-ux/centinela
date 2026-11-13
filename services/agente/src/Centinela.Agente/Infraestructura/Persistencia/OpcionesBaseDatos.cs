using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Centinela.Agente.Infraestructura.Persistencia;

/// <summary>Conexión al esquema propio. La contraseña llega como secreto en archivo (agente_db_password, ADR-005).</summary>
public sealed class OpcionesBaseDatos
{
    public const string Seccion = "BaseDatos";
    public const string ClaveSecreto = "agente_db_password";

    [Required]
    public string Host { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Puerto { get; set; } = 5432;

    [Required]
    public string Nombre { get; set; } = "centinela";

    [Required]
    public string Usuario { get; set; } = "agente";

    [Required(ErrorMessage = "Falta el secreto agente_db_password")]
    public string? Password { get; set; }

    public string CadenaConexion() => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Puerto,
        Database = Nombre,
        Username = Usuario,
        Password = Password,
        ApplicationName = "agente",
    }.ConnectionString;
}
