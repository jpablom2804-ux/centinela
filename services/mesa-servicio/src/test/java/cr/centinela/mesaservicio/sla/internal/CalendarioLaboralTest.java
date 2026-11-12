package cr.centinela.mesaservicio.sla.internal;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.DayOfWeek;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalTime;
import java.time.ZoneId;
import java.util.Set;
import org.junit.jupiter.api.Test;

/** Casos límite del tiempo hábil: fines de semana, fuera de horario, feriados y cambio de día en UTC. */
class CalendarioLaboralTest {

    private static final ZoneId CR = ZoneId.of("America/Costa_Rica"); // UTC-6, sin horario de verano
    private static final LocalDate FERIADO = LocalDate.of(2026, 9, 15); // martes

    private final CalendarioLaboral calendario = new CalendarioLaboral(CR, LocalTime.of(8, 0), LocalTime.of(17, 0),
            Set.of(DayOfWeek.MONDAY, DayOfWeek.TUESDAY, DayOfWeek.WEDNESDAY, DayOfWeek.THURSDAY, DayOfWeek.FRIDAY),
            Set.of(FERIADO));

    private static Instant cr(String fechaHora) {
        return java.time.LocalDateTime.parse(fechaHora).atZone(CR).toInstant();
    }

    @Test
    void dentro_de_la_jornada_cuenta_todo() {
        assertThat(calendario.tiempoHabil(cr("2026-11-09T09:00"), cr("2026-11-09T11:30"))).isEqualTo(Duration.ofMinutes(150));
    }

    @Test
    void antes_y_despues_del_horario_no_cuenta() {
        assertThat(calendario.tiempoHabil(cr("2026-11-09T06:00"), cr("2026-11-09T19:00"))).isEqualTo(Duration.ofHours(9));
    }

    @Test
    void el_fin_de_semana_no_cuenta() {
        // viernes 16:00 → lunes 09:00 = 1 h el viernes + 1 h el lunes
        assertThat(calendario.tiempoHabil(cr("2026-11-13T16:00"), cr("2026-11-16T09:00"))).isEqualTo(Duration.ofHours(2));
    }

    @Test
    void el_feriado_no_cuenta() {
        // lunes 16:00 → miércoles 09:00, con el martes feriado = 1 h + 1 h
        assertThat(calendario.tiempoHabil(cr("2026-09-14T16:00"), cr("2026-09-16T09:00"))).isEqualTo(Duration.ofHours(2));
    }

    @Test
    void el_dia_se_decide_en_la_zona_local_y_no_en_utc() {
        // Lunes 16:30 en CR ya es lunes 22:30 UTC, y lunes 18:30 en CR es martes 00:30 UTC: solo cuenta media hora.
        var desde = cr("2026-11-09T16:30");
        var hasta = cr("2026-11-09T18:30");
        assertThat(hasta.atZone(ZoneId.of("UTC")).getDayOfMonth()).isEqualTo(10);
        assertThat(calendario.tiempoHabil(desde, hasta)).isEqualTo(Duration.ofMinutes(30));
    }

    @Test
    void sumar_horas_habiles_salta_noches_y_fines_de_semana() {
        // viernes 15:00 + 4 h hábiles = 2 h el viernes + 2 h el lunes → lunes 10:00
        assertThat(calendario.sumar(cr("2026-11-13T15:00"), Duration.ofHours(4))).isEqualTo(cr("2026-11-16T10:00"));
    }

    @Test
    void sumar_desde_fuera_de_horario_empieza_al_abrir() {
        assertThat(calendario.sumar(cr("2026-11-09T05:00"), Duration.ofHours(1))).isEqualTo(cr("2026-11-09T09:00"));
    }

    @Test
    void intervalos_vacios_o_invertidos_valen_cero() {
        var t = cr("2026-11-09T10:00");
        assertThat(calendario.tiempoHabil(t, t)).isZero();
        assertThat(calendario.tiempoHabil(t, t.minusSeconds(60))).isZero();
    }

    @Test
    void el_reloj_pausado_no_acumula() {
        var reloj = RelojSla.iniciar(java.util.UUID.randomUUID(), cr.centinela.mesaservicio.tickets.api.Prioridad.P1, cr("2026-11-09T09:00"));
        reloj.detener(cr("2026-11-09T10:00"), calendario);
        assertThat(reloj.consumido(cr("2026-11-09T15:00"), calendario)).isEqualTo(Duration.ofHours(1));
        reloj.reanudar(cr("2026-11-09T15:00"));
        assertThat(reloj.consumido(cr("2026-11-09T16:00"), calendario)).isEqualTo(Duration.ofHours(2));
    }
}
