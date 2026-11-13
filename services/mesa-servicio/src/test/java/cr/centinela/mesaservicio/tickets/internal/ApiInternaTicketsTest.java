package cr.centinela.mesaservicio.tickets.internal;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.httpBasic;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import cr.centinela.mesaservicio.PruebaIntegracion;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import tools.jackson.databind.ObjectMapper;

/** Herramientas del agente sobre datos reales: tickets similares e incidentes abiertos, siempre enmascarados. */
@AutoConfigureMockMvc
class ApiInternaTicketsTest extends PruebaIntegracion {

    @Autowired MockMvc mvc;
    @Autowired ObjectMapper json;

    private String crear(String titulo, String descripcion, String sistema) throws Exception {
        var cuerpo = json.writeValueAsString(java.util.Map.of("tipo", "incidente", "titulo", titulo, "descripcion", descripcion,
                "sistemaReportado", sistema, "canal", "portal"));
        var respuesta = mvc.perform(post("/api/tickets").with(httpBasic("tecnico2", CLAVE_USUARIOS))
                        .contentType(MediaType.APPLICATION_JSON).content(cuerpo))
                .andExpect(status().isCreated())
                .andReturn().getResponse().getContentAsString();
        return json.readTree(respuesta).get("id").asString();
    }

    private String comoAgente(String ruta, String... parametros) throws Exception {
        var peticion = get(ruta).with(httpBasic("agente", CLAVE_API_INTERNA));
        for (var i = 0; i < parametros.length; i += 2) {
            peticion.param(parametros[i], parametros[i + 1]);
        }
        return mvc.perform(peticion).andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
    }

    @Test
    void similares_no_devuelve_el_ticket_en_curso_y_va_enmascarado() throws Exception {
        var enCurso = crear("Impresora del tercer piso atascada", "La impresora atasca cada hoja desde esta mañana.", "impresion");
        var parecido = crear("Impresora atascada en contabilidad", "Atasca las hojas; llamar al 8888-1234.", "impresion");

        var respuesta = json.readTree(comoAgente("/interna/tickets/" + enCurso + "/similares", "consulta", "impresora atascada hojas"));

        var ids = respuesta.valueStream().map(t -> t.get("id").asString()).toList();
        assertThat(ids).contains(parecido).doesNotContain(enCurso);
        assertThat(respuesta.toString()).doesNotContain("8888-1234");
    }

    @Test
    void incidentes_abiertos_del_sistema_enmascarados() throws Exception {
        var id = crear("SINPE rechaza transferencias de la cédula 1-2345-0678", "Rechaza todas las transferencias.", "sinpe");

        var respuesta = comoAgente("/interna/sistemas/sinpe/incidentes");

        assertThat(respuesta).contains(id).contains("[CEDULA]").doesNotContain("1-2345-0678");
    }

    @Test
    void el_sistema_tiene_que_ser_un_codigo_valido() throws Exception {
        mvc.perform(get("/interna/sistemas/SINPE;drop/incidentes").with(httpBasic("agente", CLAVE_API_INTERNA)))
                .andExpect(status().isBadRequest());
    }
}
