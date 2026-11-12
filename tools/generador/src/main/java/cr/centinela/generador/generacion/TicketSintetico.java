package cr.centinela.generador.generacion;

/** Un ticket listo para enviar. Los valores de tipo y canal son los códigos de la API (incidente, portal…). */
public record TicketSintetico(String tipo, String titulo, String descripcion, String sistemaReportado, String canal) {}
