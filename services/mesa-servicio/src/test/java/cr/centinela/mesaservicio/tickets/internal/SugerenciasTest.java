package cr.centinela.mesaservicio.tickets.internal;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.httpBasic;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import cr.centinela.mesaservicio.PruebaIntegracion;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.core.MessageProperties;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.web.servlet.MockMvc;
import tools.jackson.databind.ObjectMapper;

/** TicketTriado → sugerencia guardada sin aplicarse, con todo lo que no corresponde descartado o a la DLQ. */
@AutoConfigureMockMvc
class SugerenciasTest extends PruebaIntegracion {

    private static final String DLQ = "mesa-servicio.tickets-triados.dlq";

    @Autowired MockMvc mvc;
    @Autowired ObjectMapper json;
    @Autowired JdbcTemplate jdbc;

    @BeforeEach
    void vaciarDlq() {
        rabbitComoAdmin().execute(canal -> canal.queuePurge(DLQ));
    }

    private String crearTicket() throws Exception {
        var respuesta = mvc.perform(post("/api/tickets").with(httpBasic("tecnico2", CLAVE_USUARIOS)).contentType(MediaType.APPLICATION_JSON)
                        .content("""
                                {"tipo":"incidente","titulo":"VPN no conecta","descripcion":"Se queda en conectando.","sistemaReportado":"vpn","canal":"portal"}
                                """))
                .andExpect(status().isCreated())
                .andReturn().getResponse().getContentAsString();
        return json.readTree(respuesta).get("id").asString();
    }

    private static Map<String, Object> datos(String ticketId) {
        var datos = new LinkedHashMap<String, Object>();
        datos.put("ticketId", ticketId);
        datos.put("estadoAnalisis", "completado");
        datos.put("categoria", "red_y_conectividad");
        datos.put("sistemaAfectado", "vpn");
        datos.put("prioridadSugerida", "P1");
        datos.put("justificacion", "Varias personas sin VPN.");
        datos.put("duplicadosPosibles", List.of());
        datos.put("articulosSugeridos", List.of("KB-002"));
        datos.put("borradorRespuesta", "Estamos revisando la VPN.");
        datos.put("metricas", Map.of("modelo", "modelo-falso", "tokensEntrada", 1500, "tokensSalida", 300, "iteraciones", 2, "duracionMs", 900));
        return datos;
    }

    private void publicar(UUID eventoId, Map<String, Object> datos) {
        var sobre = new LinkedHashMap<String, Object>();
        sobre.put("id", eventoId.toString());
        sobre.put("tipo", "TicketTriado");
        sobre.put("version", 1);
        sobre.put("correlationId", "a1b2c3d4e5f60718293a4b5c6d7e8f90");
        sobre.put("ocurridoEn", "2026-11-12T15:00:00.000Z");
        sobre.put("datos", datos);
        var propiedades = new MessageProperties();
        propiedades.setContentType(MessageProperties.CONTENT_TYPE_JSON);
        propiedades.setCorrelationId("a1b2c3d4e5f60718293a4b5c6d7e8f90");
        propiedades.setMessageId(eventoId.toString());
        rabbitComoAdmin().send("triajes", "ticket.triado", new Message(json.writeValueAsBytes(sobre), propiedades));
    }

    private int sugerencias(String ticketId) {
        return jdbc.queryForObject("select count(*) from mesa_servicio.sugerencia_triaje where ticket_id = ?::uuid", Integer.class, ticketId);
    }

    private void esperarHasta(java.util.function.BooleanSupplier condicion) throws InterruptedException {
        var limite = Instant.now().plus(Duration.ofSeconds(15));
        while (!condicion.getAsBoolean() && Instant.now().isBefore(limite)) {
            Thread.sleep(100);
        }
        assertThat(condicion.getAsBoolean()).as("la condición no se cumplió a tiempo").isTrue();
    }

    private Message enDlq() {
        return rabbitComoAdmin().receive(DLQ, 15_000);
    }

    @Test
    void guarda_la_sugerencia_sin_aplicarla() throws Exception {
        var id = crearTicket();

        publicar(UUID.randomUUID(), datos(id));
        esperarHasta(() -> sugerencias(id) == 1);

        // Nada del ticket cambió: el agente dijo P1, pero la prioridad la decide un técnico.
        mvc.perform(get("/api/tickets/" + id).with(httpBasic("tecnico1", CLAVE_USUARIOS)))
                .andExpect(jsonPath("$.prioridad").value("P3"))
                .andExpect(jsonPath("$.estado").value("nuevo"));
        mvc.perform(get("/api/tickets/" + id + "/sugerencia").with(httpBasic("tecnico1", CLAVE_USUARIOS)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.prioridadSugerida").value("P1"))
                .andExpect(jsonPath("$.articulosSugeridos[0]").value("KB-002"))
                .andExpect(jsonPath("$.aplicada").value(false));
        assertThat(jdbc.queryForObject("select count(*) from auditoria.bitacora where accion = 'SUGERENCIA_TRIAJE_RECIBIDA' and recurso_id = ?",
                Integer.class, id)).isEqualTo(1);
    }

    @Test
    void solo_soporte_y_auditoria_ven_la_sugerencia() throws Exception {
        mvc.perform(get("/api/tickets/" + crearTicket() + "/sugerencia").with(httpBasic("solicitante1", CLAVE_USUARIOS)))
                .andExpect(status().isForbidden());
        mvc.perform(get("/api/tickets/" + UUID.randomUUID() + "/sugerencia").with(httpBasic("solicitante1", CLAVE_USUARIOS)))
                .andExpect(status().isForbidden());
    }

    @Test
    void el_mismo_evento_dos_veces_guarda_una_sola_sugerencia() throws Exception {
        var id = crearTicket();
        var evento = UUID.randomUUID();

        publicar(evento, datos(id));
        publicar(evento, datos(id));
        esperarHasta(() -> sugerencias(id) == 1);
        Thread.sleep(1_000);

        assertThat(sugerencias(id)).isEqualTo(1);
        assertThat(rabbitComoAdmin().receive(DLQ, 500)).isNull();
    }

    @Test
    void un_texto_fuera_de_limite_va_a_la_dlq() throws Exception {
        var id = crearTicket();
        var datos = datos(id);
        datos.put("justificacion", "x".repeat(501));

        publicar(UUID.randomUUID(), datos);

        assertThat(enDlq()).isNotNull();
        assertThat(sugerencias(id)).isZero();
    }

    @Test
    void un_articulo_que_no_existe_va_a_la_dlq() throws Exception {
        var id = crearTicket();
        var datos = datos(id);
        datos.put("articulosSugeridos", List.of("KB-999"));

        publicar(UUID.randomUUID(), datos);

        var muerto = enDlq();
        assertThat(muerto).isNotNull();
        assertThat(new String(muerto.getBody(), StandardCharsets.UTF_8)).contains("KB-999");
        assertThat(sugerencias(id)).isZero();
    }

    @Test
    void si_un_tecnico_ya_lo_trabajo_la_sugerencia_se_descarta() throws Exception {
        // A3: un TicketTriado tardío (o falso) no pisa el trabajo de una persona.
        var id = crearTicket();
        mvc.perform(post("/api/tickets/" + id + "/estado").with(httpBasic("tecnico1", CLAVE_USUARIOS))
                        .contentType(MediaType.APPLICATION_JSON).content("{\"estado\":\"triado\"}"))
                .andExpect(status().isOk());

        publicar(UUID.randomUUID(), datos(id));
        Thread.sleep(2_000);

        assertThat(sugerencias(id)).isZero();
        assertThat(rabbitComoAdmin().receive(DLQ, 500)).isNull();
    }
}
