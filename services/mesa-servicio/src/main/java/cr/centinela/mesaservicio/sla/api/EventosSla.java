package cr.centinela.mesaservicio.sla.api;

import cr.centinela.mesaservicio.tickets.api.Prioridad;
import java.time.Instant;
import java.util.UUID;

/** Eventos en memoria del SLA de resolución (docs/EVENTOS.md, "Eventos internos de mesa-servicio"). */
public final class EventosSla {

    private EventosSla() {}

    public record SlaPorVencer(UUID ticketId, Prioridad prioridad, int porcentajeConsumido, Instant venceEn) {}

    public record SlaVencido(UUID ticketId, Prioridad prioridad, Instant vencioEn) {}
}
