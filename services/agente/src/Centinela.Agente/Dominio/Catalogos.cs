namespace Centinela.Agente.Dominio;

/// <summary>Valores permitidos en la sugerencia del agente. Lo que no esté aquí se rechaza.</summary>
public static class Catalogos
{
    public static readonly IReadOnlyList<string> Categorias =
    [
        "acceso_y_cuentas", "hardware", "software", "red_y_conectividad",
        "correo_y_colaboracion", "aplicaciones_de_negocio", "seguridad", "otro",
    ];

    public static readonly IReadOnlyList<string> Sistemas =
    [
        "core_bancario", "banca_en_linea", "app_movil", "sinpe", "tarjetas", "correo", "vpn",
        "directorio_activo", "impresion", "estaciones_de_trabajo", "red_interna", "otro",
    ];

    public static readonly IReadOnlyList<string> Prioridades = ["P1", "P2", "P3", "P4"];
}

/// <summary>Límites de la salida del modelo, iguales al contrato TicketTriado de docs/EVENTOS.md.</summary>
public static class LimitesSalida
{
    public const int MaxJustificacion = 500;
    public const int MaxBorradorRespuesta = 2000;
    public const int MaxIds = 5;
}
