package cr.centinela.mesaservicio.sla.internal;

import cr.centinela.mesaservicio.sla.api.EventosSla;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

/**
 * Consumidor de los avisos de SLA. Por ahora los deja en el log (solo IDs y prioridad); las notificaciones
 * al coordinador y el escalamiento llegan con el frontend (fase 4).
 */
@Component
class AvisosSla {

    private static final Logger log = LoggerFactory.getLogger(AvisosSla.class);

    @EventListener
    void porVencer(EventosSla.SlaPorVencer aviso) {
        log.warn("SLA por vencer: ticket {} ({}), {} % consumido, vence {}", aviso.ticketId(), aviso.prioridad(),
                aviso.porcentajeConsumido(), aviso.venceEn());
    }

    @EventListener
    void vencido(EventosSla.SlaVencido aviso) {
        log.error("SLA vencido: ticket {} ({}), escalar al coordinador", aviso.ticketId(), aviso.prioridad());
    }
}
