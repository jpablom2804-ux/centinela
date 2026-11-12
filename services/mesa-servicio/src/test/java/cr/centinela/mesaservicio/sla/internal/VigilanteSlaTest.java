package cr.centinela.mesaservicio.sla.internal;

import static org.assertj.core.api.Assertions.assertThat;

import cr.centinela.mesaservicio.PruebaIntegracion;
import cr.centinela.mesaservicio.sla.api.EventosSla;
import cr.centinela.mesaservicio.tickets.api.Prioridad;
import java.time.Duration;
import java.time.Instant;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.event.ApplicationEvents;
import org.springframework.test.context.event.RecordApplicationEvents;
import org.springframework.transaction.support.TransactionTemplate;

/** Flujo RELOJ_SLA: aviso al 75 % y al vencer, contando solo tiempo hábil. */
@RecordApplicationEvents
class VigilanteSlaTest extends PruebaIntegracion {

    @Autowired VigilanteSla vigilante;
    @Autowired RelojMovible reloj;
    @Autowired RelojSlaRepositorio relojes;
    @Autowired TransactionTemplate tx;
    @Autowired JdbcTemplate jdbc;
    @Autowired ApplicationEvents eventos;

    @Test
    void avisa_al_75_por_ciento_y_al_vencer_un_p1() {
        var lunes9 = Instant.parse("2026-11-09T15:00:00Z"); // lunes 09:00 en Costa Rica
        reloj.fijar(lunes9);
        var ticketId = UUID.randomUUID();
        // El reloj necesita un ticket existente (clave foránea): se inserta uno mínimo directamente.
        jdbc.update("""
                insert into mesa_servicio.ticket (id, tipo, titulo, descripcion, canal, estado, prioridad, solicitante, creado_en, actualizado_en, version)
                values (?, 'INCIDENTE', 'Core caído', 'No responde', 'PORTAL', 'EN_PROGRESO', 'P1', 'solicitante1', now(), now(), 0)
                """, ticketId);
        tx.executeWithoutResult(s -> relojes.save(RelojSla.iniciar(ticketId, Prioridad.P1, lunes9)));

        // P1 = 4 h hábiles. A las 3 h 05 min (77 %) avisa que está por vencer.
        reloj.avanzar(Duration.ofMinutes(185));
        vigilante.revisar();
        assertThat(eventos.stream(EventosSla.SlaPorVencer.class).filter(e -> e.ticketId().equals(ticketId)).toList())
                .singleElement()
                .satisfies(e -> assertThat(e.porcentajeConsumido()).isEqualTo(77));

        // Una segunda revisión no repite el aviso.
        vigilante.revisar();
        assertThat(eventos.stream(EventosSla.SlaPorVencer.class).filter(e -> e.ticketId().equals(ticketId)).count()).isEqualTo(1);

        // Lunes 13:30 en Costa Rica: 4 h 30 min hábiles consumidas de 4 h → vencido.
        reloj.fijar(Instant.parse("2026-11-09T19:30:00Z"));
        vigilante.revisar();
        assertThat(eventos.stream(EventosSla.SlaVencido.class).filter(e -> e.ticketId().equals(ticketId)).count()).isEqualTo(1);
        assertThat(jdbc.queryForList("select accion from auditoria.bitacora where recurso_id = ? order by id", String.class, ticketId.toString()))
                .containsExactly("SLA_POR_VENCER", "SLA_VENCIDO");
    }

    @Test
    void el_tiempo_fuera_de_jornada_no_vence_el_sla() {
        var viernes16 = Instant.parse("2026-11-13T22:00:00Z"); // viernes 16:00 en Costa Rica
        reloj.fijar(viernes16);
        var ticketId = UUID.randomUUID();
        jdbc.update("""
                insert into mesa_servicio.ticket (id, tipo, titulo, descripcion, canal, estado, prioridad, solicitante, creado_en, actualizado_en, version)
                values (?, 'INCIDENTE', 'Impresora', 'No imprime', 'PORTAL', 'EN_PROGRESO', 'P1', 'solicitante1', now(), now(), 0)
                """, ticketId);
        tx.executeWithoutResult(s -> relojes.save(RelojSla.iniciar(ticketId, Prioridad.P1, viernes16)));

        // Todo el fin de semana después: solo corrió 1 h hábil (25 %) → ni aviso ni vencimiento.
        reloj.fijar(Instant.parse("2026-11-16T13:00:00Z")); // lunes 07:00 en Costa Rica
        vigilante.revisar();

        assertThat(eventos.stream(EventosSla.SlaPorVencer.class).filter(e -> e.ticketId().equals(ticketId))).isEmpty();
        assertThat(eventos.stream(EventosSla.SlaVencido.class).filter(e -> e.ticketId().equals(ticketId))).isEmpty();
    }

    @Test
    void cambiar_la_prioridad_cambia_el_objetivo() {
        var lunes9 = Instant.parse("2026-11-09T15:00:00Z");
        reloj.fijar(lunes9);
        var ticketId = UUID.randomUUID();
        jdbc.update("""
                insert into mesa_servicio.ticket (id, tipo, titulo, descripcion, canal, estado, prioridad, solicitante, creado_en, actualizado_en, version)
                values (?, 'INCIDENTE', 'Correo', 'No llega', 'PORTAL', 'EN_PROGRESO', 'P4', 'solicitante1', now(), now(), 0)
                """, ticketId);
        tx.executeWithoutResult(s -> relojes.save(RelojSla.iniciar(ticketId, Prioridad.P4, lunes9)));
        reloj.avanzar(Duration.ofHours(4));
        vigilante.revisar(); // P4 = 72 h: 4 h no avisan
        assertThat(eventos.stream(EventosSla.SlaVencido.class).filter(e -> e.ticketId().equals(ticketId))).isEmpty();

        // Si un técnico la sube a P1 (4 h), ya está vencido.
        tx.executeWithoutResult(s -> relojes.findById(ticketId).orElseThrow().cambiarPrioridad(Prioridad.P1));
        vigilante.revisar();
        assertThat(eventos.stream(EventosSla.SlaVencido.class).filter(e -> e.ticketId().equals(ticketId)).count()).isEqualTo(1);
    }
}
