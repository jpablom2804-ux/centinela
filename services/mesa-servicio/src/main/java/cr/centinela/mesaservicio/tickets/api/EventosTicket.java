package cr.centinela.mesaservicio.tickets.api;

import java.time.Instant;
import java.util.UUID;

/**
 * Eventos de aplicación en memoria (no van por RabbitMQ, ADR-003). Se publican dentro de la transacción
 * del cambio, así los módulos que escuchan (sla) quedan consistentes con el ticket.
 */
public final class EventosTicket {

    private EventosTicket() {}

    public record TicketCreado(UUID ticketId, Prioridad prioridad, Instant creadoEn, String actor) {}

    public record EstadoTicketCambiado(UUID ticketId, EstadoTicket anterior, EstadoTicket nuevo, Instant momento, String actor) {}

    public record PrioridadTicketCambiada(UUID ticketId, Prioridad anterior, Prioridad nueva, Instant momento, String actor) {}
}
