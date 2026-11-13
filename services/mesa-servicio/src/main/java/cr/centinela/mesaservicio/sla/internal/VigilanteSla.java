package cr.centinela.mesaservicio.sla.internal;

import cr.centinela.mesaservicio.auditoria.EventoAuditoria;
import cr.centinela.mesaservicio.auditoria.RegistroAuditoria;
import cr.centinela.mesaservicio.sla.api.EventosSla;
import java.time.Clock;
import java.util.Map;
import net.javacrumbs.shedlock.spring.annotation.SchedulerLock;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.context.annotation.Profile;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/**
 * Revisa periódicamente los relojes que corren y avisa al pasar el umbral (75 %) y al vencer. Con varias
 * réplicas, ShedLock asegura que solo una revise a la vez (AGENTS.md).
 */
@Component
// El proceso de migración no revisa SLA: solo migra y termina (A14).
@Profile("!migracion")
class VigilanteSla {

    private static final String SISTEMA = "sistema:sla";

    private final RelojSlaRepositorio relojes;
    private final PoliticasSla politicas;
    private final RegistroAuditoria auditoria;
    private final ApplicationEventPublisher eventos;
    private final Clock reloj;

    VigilanteSla(RelojSlaRepositorio relojes, PoliticasSla politicas, RegistroAuditoria auditoria,
            ApplicationEventPublisher eventos, Clock reloj) {
        this.relojes = relojes;
        this.politicas = politicas;
        this.auditoria = auditoria;
        this.eventos = eventos;
        this.reloj = reloj;
    }

    // [FLUJO RELOJ_SLA 1] Tarea programada con bloqueo en la base: una sola réplica revisa a la vez → paso 2
    @Scheduled(fixedDelayString = "${centinela.sla.revision:PT1M}")
    @SchedulerLock(name = "vigilante-sla", lockAtMostFor = "PT5M")
    @Transactional
    public void revisar() {
        var ahora = reloj.instant();
        var calendario = politicas.calendario();
        for (var r : relojes.findByCorriendoTrueAndVencidoFalse()) {
            // [FLUJO RELOJ_SLA 2] Calcula el tiempo hábil consumido contra el objetivo de su prioridad → paso 3
            var objetivo = politicas.objetivo(r.prioridad());
            var consumido = r.consumido(ahora, calendario);
            var fraccion = (double) consumido.toMillis() / objetivo.toMillis();

            // [FLUJO RELOJ_SLA 3] Publica SlaVencido o SlaPorVencer en memoria y lo registra en la bitácora → AvisosSla
            if (fraccion >= 1.0) {
                r.marcarVencido();
                auditar("SLA_VENCIDO", r, (int) Math.floor(fraccion * 100));
                eventos.publishEvent(new EventosSla.SlaVencido(r.ticketId(), r.prioridad(), ahora));
            } else if (fraccion >= politicas.umbralAlerta() && !r.alertaEnviada()) {
                r.marcarAlerta();
                var porcentaje = (int) Math.floor(fraccion * 100);
                var venceEn = calendario.sumar(ahora, objetivo.minus(consumido));
                auditar("SLA_POR_VENCER", r, porcentaje);
                eventos.publishEvent(new EventosSla.SlaPorVencer(r.ticketId(), r.prioridad(), porcentaje, venceEn));
            }
        }
    }

    private void auditar(String accion, RelojSla r, int porcentaje) {
        auditoria.registrar(new EventoAuditoria(SISTEMA, accion, "ticket", r.ticketId().toString(), null,
                Map.of("prioridad", r.prioridad().name(), "porcentajeConsumido", porcentaje)));
    }
}
