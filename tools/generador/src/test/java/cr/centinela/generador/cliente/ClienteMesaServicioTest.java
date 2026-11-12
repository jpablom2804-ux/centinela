package cr.centinela.generador.cliente;

import static org.assertj.core.api.Assertions.assertThat;

import cr.centinela.generador.generacion.TicketSintetico;
import org.junit.jupiter.api.Test;

class ClienteMesaServicioTest {

    @Test
    void arma_el_json_que_espera_la_api() {
        var json = ClienteMesaServicio.aJson(new TicketSintetico("incidente", "VPN", "Se cae", null, "portal"));

        assertThat(json).isEqualTo("{\"tipo\":\"incidente\",\"titulo\":\"VPN\",\"descripcion\":\"Se cae\",\"sistemaReportado\":null,\"canal\":\"portal\"}");
    }

    @Test
    void escapa_comillas_barras_saltos_y_controles() {
        assertThat(ClienteMesaServicio.escapar("dijo \"hola\"\\\n\u0001")).isEqualTo("dijo \\\"hola\\\"\\\\\\n\\u0001");
    }

    @Test
    void conserva_los_acentos_tal_cual() {
        assertThat(ClienteMesaServicio.escapar("mañana sesión")).isEqualTo("mañana sesión");
    }
}
