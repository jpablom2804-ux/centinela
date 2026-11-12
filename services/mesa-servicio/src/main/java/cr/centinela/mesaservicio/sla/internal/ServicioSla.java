package cr.centinela.mesaservicio.sla.internal;

import cr.centinela.mesaservicio.auditoria.EventoAuditoria;
import cr.centinela.mesaservicio.auditoria.RegistroAuditoria;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import cr.centinela.mesaservicio.tickets.api.EventosTicket;
import java.util.Map;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Service;

/**
 * Mantiene el reloj de cada ticket a partir de los eventos del módulo de tickets. Escucha en la misma
 * transacción del cambio: si el ticket se guarda, el reloj también, y si falla, ninguno.
 */
@Service
class ServicioSla {

    private static final String RECURSO = "ticket";

    private final RelojSlaRepositorio relojes;
    private final PoliticasSla politicas;
    private final RegistroAuditoria auditoria;

    ServicioSla(RelojSlaRepositorio relojes, PoliticasSla politicas, RegistroAuditoria auditoria) {
        this.relojes = relojes;
        this.politicas = politicas;
        this.auditoria = auditoria;
    }

    // [FLUJO CREAR_TICKET 5] Arranca el reloj de resolución del ticket nuevo → fin del flujo (vuelve el 201 al controlador)
    @EventListener
    void alCrearTicket(EventosTicket.TicketCreado evento) {
        relojes.save(RelojSla.iniciar(evento.ticketId(), evento.prioridad(), evento.creadoEn()));
    }

    /** en_espera pausa; volver a trabajar reanuda; resuelto o cerrado detiene. Cada pausa queda en la bitácora. */
    @EventListener
    void alCambiarEstado(EventosTicket.EstadoTicketCambiado evento) {
        var reloj = relojes.findById(evento.ticketId()).orElseThrow();
        var calendario = politicas.calendario();
        var destino = evento.nuevo();

        if (destino == EstadoTicket.EN_ESPERA || destino == EstadoTicket.RESUELTO || destino == EstadoTicket.CERRADO) {
            if (reloj.corriendo()) {
                reloj.detener(evento.momento(), calendario);
                auditar(evento.actor(), destino == EstadoTicket.EN_ESPERA ? "SLA_PAUSADO" : "SLA_DETENIDO", evento, reloj);
            }
        } else if (!reloj.corriendo()) {
            reloj.reanudar(evento.momento());
            auditar(evento.actor(), "SLA_REANUDADO", evento, reloj);
        }
    }

    @EventListener
    void alCambiarPrioridad(EventosTicket.PrioridadTicketCambiada evento) {
        relojes.findById(evento.ticketId()).orElseThrow().cambiarPrioridad(evento.nueva());
    }

    private void auditar(String actor, String accion, EventosTicket.EstadoTicketCambiado evento, RelojSla reloj) {
        auditoria.registrar(new EventoAuditoria(actor, accion, RECURSO, evento.ticketId().toString(), null,
                Map.of("estado", evento.nuevo().codigo(), "prioridad", reloj.prioridad().name())));
    }
}
