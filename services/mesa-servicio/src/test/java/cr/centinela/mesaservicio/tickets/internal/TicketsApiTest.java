package cr.centinela.mesaservicio.tickets.internal;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.httpBasic;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import cr.centinela.mesaservicio.PruebaIntegracion;
import cr.centinela.mesaservicio.auditoria.RegistroAuditoria;
import java.util.UUID;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import tools.jackson.databind.ObjectMapper;

@AutoConfigureMockMvc
class TicketsApiTest extends PruebaIntegracion {

    private static final String TICKET = """
            {"tipo":"incidente","titulo":"VPN no conecta","descripcion":"Se queda en conectando.","sistemaReportado":"vpn","canal":"portal"}
            """;

    @Autowired MockMvc mvc;
    @Autowired ObjectMapper json;
    @Autowired JdbcTemplate jdbc;
    @Autowired RegistroAuditoria auditoria;

    private ResultActions como(String usuario, org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder peticion) throws Exception {
        return mvc.perform(peticion.with(httpBasic(usuario, CLAVE_USUARIOS)).contentType(MediaType.APPLICATION_JSON));
    }

    private String crearTicket(String usuario) throws Exception {
        var respuesta = como(usuario, post("/api/tickets").content(TICKET))
                .andExpect(status().isCreated())
                .andReturn().getResponse().getContentAsString();
        return json.readTree(respuesta).get("id").asString();
    }

    private void cambiarEstado(String usuario, String id, String estado, String justificacion, int esperado) throws Exception {
        var cuerpo = justificacion == null
                ? "{\"estado\":\"" + estado + "\"}"
                : "{\"estado\":\"" + estado + "\",\"justificacion\":\"" + justificacion + "\"}";
        como(usuario, post("/api/tickets/" + id + "/estado").content(cuerpo)).andExpect(status().is(esperado));
    }

    @Test
    void crear_deja_el_ticket_nuevo_con_bitacora_y_reloj_de_sla() throws Exception {
        var id = crearTicket("solicitante1");

        como("solicitante1", get("/api/tickets/" + id))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.estado").value("nuevo"))
                .andExpect(jsonPath("$.prioridad").value("P3"))
                .andExpect(jsonPath("$.solicitante").value("solicitante1"));
        assertThat(jdbc.queryForObject("select count(*) from auditoria.bitacora where accion = 'TICKET_CREADO' and recurso_id = ?",
                Integer.class, id)).isEqualTo(1);
        assertThat(jdbc.queryForObject("select corriendo from mesa_servicio.sla_reloj where ticket_id = ?::uuid", Boolean.class, id)).isTrue();
    }

    @Test
    void sin_credenciales_no_entra() throws Exception {
        mvc.perform(get("/api/tickets")).andExpect(status().isUnauthorized());
    }

    @Test
    void un_campo_que_la_accion_no_acepta_es_400() throws Exception {
        // A6: al crear, el solicitante no puede fijar la prioridad ni el estado.
        var conPrioridad = TICKET.replace("\"canal\":\"portal\"", "\"canal\":\"portal\",\"prioridad\":\"P1\"");
        como("solicitante1", post("/api/tickets").content(conPrioridad))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.codigo").value("DATOS_INVALIDOS"));
    }

    @Test
    void un_titulo_demasiado_largo_es_400() throws Exception {
        // A4: largos máximos validados en la API.
        var largo = TICKET.replace("VPN no conecta", "x".repeat(201));
        como("solicitante1", post("/api/tickets").content(largo)).andExpect(status().isBadRequest());
    }

    @Test
    void ticket_ajeno_e_inexistente_responden_exactamente_igual() throws Exception {
        var id = crearTicket("solicitante1");

        var ajeno = como("solicitante2", get("/api/tickets/" + id)).andExpect(status().isNotFound()).andReturn().getResponse().getContentAsString();
        var inexistente = como("solicitante2", get("/api/tickets/" + UUID.randomUUID())).andExpect(status().isNotFound())
                .andReturn().getResponse().getContentAsString();

        assertThat(ajeno).isEqualTo(inexistente);
    }

    @Test
    void el_solicitante_no_mueve_su_ticket_por_el_flujo_de_soporte() throws Exception {
        var id = crearTicket("solicitante1");
        cambiarEstado("solicitante1", id, "asignado", null, 403);
        como("solicitante1", post("/api/tickets/" + id + "/prioridad").content("{\"prioridad\":\"P1\",\"justificacion\":\"urgente\"}"))
                .andExpect(status().isForbidden());
    }

    @Test
    void el_auditor_ve_todo_pero_no_modifica() throws Exception {
        var id = crearTicket("solicitante1");
        como("auditor1", get("/api/tickets/" + id)).andExpect(status().isOk());
        cambiarEstado("auditor1", id, "asignado", null, 403);
    }

    @Test
    void ciclo_completo_con_pausa_de_sla_cierre_y_reapertura() throws Exception {
        var id = crearTicket("solicitante1");
        como("tecnico1", post("/api/tickets/" + id + "/asignacion").content("{\"tecnico\":\"tecnico1\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.estado").value("asignado"));
        cambiarEstado("tecnico1", id, "en_progreso", null, 200);

        cambiarEstado("tecnico1", id, "en_espera", null, 200);
        assertThat(jdbc.queryForObject("select corriendo from mesa_servicio.sla_reloj where ticket_id = ?::uuid", Boolean.class, id)).isFalse();
        cambiarEstado("tecnico1", id, "en_progreso", null, 200);
        assertThat(jdbc.queryForObject("select corriendo from mesa_servicio.sla_reloj where ticket_id = ?::uuid", Boolean.class, id)).isTrue();

        cambiarEstado("tecnico1", id, "resuelto", null, 200);
        cambiarEstado("solicitante1", id, "cerrado", null, 200);

        // Un ticket cerrado no se edita: se reabre con justificación.
        como("solicitante1", post("/api/tickets/" + id + "/comentarios").content("{\"texto\":\"sigue fallando\"}"))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.codigo").value("TICKET_CERRADO"));
        cambiarEstado("solicitante1", id, "en_progreso", null, 409);
        cambiarEstado("solicitante1", id, "en_progreso", "Volvió a fallar hoy", 200);

        var acciones = jdbc.queryForList("select accion from auditoria.bitacora where recurso_id = ? order by id", String.class, id);
        assertThat(acciones).contains("TICKET_CREADO", "TICKET_ASIGNADO", "SLA_PAUSADO", "SLA_REANUDADO", "SLA_DETENIDO", "TICKET_REABIERTO");
        assertThat(auditoria.primerRegistroAlterado()).isNull();
    }

    @Test
    void transicion_invalida_es_409() throws Exception {
        var id = crearTicket("solicitante1");
        cambiarEstado("coordinador1", id, "resuelto", null, 409);
    }
}
