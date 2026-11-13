using System.ComponentModel.DataAnnotations;

namespace Centinela.Agente.Infraestructura.Mensajeria;

/// <summary>
/// RabbitMQ con el usuario propio del agente (A3: solo lee su cola y solo escribe en "triajes"). La contraseña
/// llega como secreto en archivo (rabbitmq_agente_password, ADR-005). Colas y exchanges los declara
/// deploy/rabbitmq, no el agente.
/// </summary>
public sealed class OpcionesMensajeria
{
    public const string Seccion = "Mensajeria";
    public const string ClaveSecreto = "rabbitmq_agente_password";

    [Required]
    public string Host { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Puerto { get; set; } = 5672;

    [Required]
    public string VirtualHost { get; set; } = "centinela";

    [Required]
    public string Usuario { get; set; } = "agente";

    [Required(ErrorMessage = "Falta el secreto rabbitmq_agente_password")]
    public string? Password { get; set; }

    [Required]
    public string Cola { get; set; } = "agente.tickets-creados";

    [Required]
    public string ExchangeTriajes { get; set; } = "triajes";

    [Required]
    public string RutaTriajes { get; set; } = "ticket.triado";

    /// <summary>Reintentos de un mensaje ante fallas de infraestructura (base de datos, broker) antes de mandarlo a la DLQ.</summary>
    [Range(0, 10)]
    public int MaxReintentos { get; set; } = 3;

    [Range(0, 60_000)]
    public int BackoffBaseMs { get; set; } = 1_000;

    [Range(1, 60)]
    public int EsperaConfirmacionSegundos { get; set; } = 10;
}
