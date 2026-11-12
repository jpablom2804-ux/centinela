package cr.centinela.mesaservicio.tickets.internal;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import cr.centinela.mesaservicio.plataforma.Errores;
import cr.centinela.mesaservicio.tickets.api.Canal;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import cr.centinela.mesaservicio.tickets.api.Prioridad;
import cr.centinela.mesaservicio.tickets.api.TipoTicket;
import java.time.Instant;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

class TicketTest {

    private static final Instant AHORA = Instant.parse("2026-11-12T15:00:00Z");

    private static Ticket nuevo() {
        return Ticket.crear(TipoTicket.INCIDENTE, "VPN", "No conecta", "vpn", Canal.PORTAL, "solicitante1", AHORA);
    }

    @Test
    void nace_nuevo_con_prioridad_inicial() {
        var t = nuevo();
        assertThat(t.estado()).isEqualTo(EstadoTicket.NUEVO);
        assertThat(t.prioridad()).isEqualTo(Prioridad.P3);
    }

    @ParameterizedTest
    @CsvSource({"NUEVO,EN_PROGRESO", "NUEVO,RESUELTO", "ASIGNADO,CERRADO", "EN_ESPERA,RESUELTO", "CERRADO,NUEVO"})
    void rechaza_transiciones_invalidas(EstadoTicket desde, EstadoTicket hacia) {
        assertThat(desde.puedePasarA(hacia)).isFalse();
    }

    @Test
    void recorre_el_ciclo_completo_y_se_reabre() {
        var t = nuevo();
        for (var e : new EstadoTicket[] {EstadoTicket.ASIGNADO, EstadoTicket.EN_PROGRESO, EstadoTicket.EN_ESPERA,
                EstadoTicket.EN_PROGRESO, EstadoTicket.RESUELTO, EstadoTicket.CERRADO, EstadoTicket.EN_PROGRESO}) {
            t.cambiarEstado(e, AHORA);
        }
        assertThat(t.estado()).isEqualTo(EstadoTicket.EN_PROGRESO);
    }

    @Test
    void una_transicion_invalida_lanza_regla_de_negocio() {
        assertThatThrownBy(() -> nuevo().cambiarEstado(EstadoTicket.CERRADO, AHORA))
                .isInstanceOf(Errores.ReglaDeNegocio.class)
                .hasMessageContaining("nuevo");
    }

    @Test
    void un_ticket_cerrado_no_se_edita() {
        var t = nuevo();
        t.cambiarEstado(EstadoTicket.ASIGNADO, AHORA);
        t.cambiarEstado(EstadoTicket.EN_PROGRESO, AHORA);
        t.cambiarEstado(EstadoTicket.RESUELTO, AHORA);
        t.cambiarEstado(EstadoTicket.CERRADO, AHORA);
        assertThatThrownBy(() -> t.cambiarPrioridad(Prioridad.P1, AHORA)).isInstanceOf(Errores.ReglaDeNegocio.class);
        assertThatThrownBy(() -> t.asignar("tecnico1", AHORA)).isInstanceOf(Errores.ReglaDeNegocio.class);
    }

    @Test
    void reabrir_solo_desde_resuelto_o_cerrado() {
        assertThat(EstadoTicket.CERRADO.esReapertura(EstadoTicket.EN_PROGRESO)).isTrue();
        assertThat(EstadoTicket.RESUELTO.esReapertura(EstadoTicket.EN_PROGRESO)).isTrue();
        assertThat(EstadoTicket.EN_ESPERA.esReapertura(EstadoTicket.EN_PROGRESO)).isFalse();
    }
}
