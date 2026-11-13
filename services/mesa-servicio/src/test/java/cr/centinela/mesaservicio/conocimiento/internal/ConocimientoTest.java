package cr.centinela.mesaservicio.conocimiento.internal;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.httpBasic;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import cr.centinela.mesaservicio.PruebaIntegracion;
import cr.centinela.mesaservicio.plataforma.Enmascarador;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

/** Artículos e índice semántico (ADR-007): por texto sin embeddings, por significado con ellos, y siempre enmascarado. */
@AutoConfigureMockMvc
class ConocimientoTest extends PruebaIntegracion {

    @Autowired MockMvc mvc;
    @Autowired ProveedorEmbeddingsFalso proveedor;
    @Autowired IndexadorSemantico indexador;
    @Autowired JdbcTemplate jdbc;

    @AfterEach
    void apagarEmbeddings() {
        proveedor.habilitar(false);
    }

    private static MockHttpServletRequestBuilder comoAgente(MockHttpServletRequestBuilder peticion) {
        return peticion.with(httpBasic("agente", CLAVE_API_INTERNA));
    }

    private void indexarTodo() {
        while (indexador.indexarPendientes() > 0) {
            // Lotes hasta que no quede nada pendiente.
        }
    }

    @Test
    void sin_embeddings_busca_articulos_por_texto() throws Exception {
        mvc.perform(comoAgente(get("/interna/articulos").param("consulta", "vpn contraseña vencida certificado")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[0].id").value("KB-002"))
                .andExpect(jsonPath("$[0].metodo").value("texto"));
    }

    @Test
    void con_embeddings_busca_por_significado() throws Exception {
        proveedor.habilitar(true);
        indexarTodo();

        mvc.perform(comoAgente(get("/interna/articulos").param("consulta", "la vpn no conecta, certificado y contraseña vencida")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[0].id").value("KB-002"))
                .andExpect(jsonPath("$[0].metodo").value("significado"));
        assertThat(jdbc.queryForObject("select count(*) from mesa_servicio.indice_semantico where recurso_tipo = 'articulo' and embedding is null",
                Integer.class)).isZero();
    }

    @Test
    void lo_que_sale_hacia_el_proveedor_de_embeddings_va_enmascarado() throws Exception {
        proveedor.habilitar(true);
        mvc.perform(post("/api/tickets").with(httpBasic("tecnico2", CLAVE_USUARIOS)).contentType(MediaType.APPLICATION_JSON)
                        .content("""
                                {"tipo":"incidente","titulo":"Bloqueo de cuenta","descripcion":"Mi cédula es 1-2345-0678 y mi correo ana@banco.cr",\
                                "sistemaReportado":"directorio_activo","canal":"portal"}
                                """))
                .andExpect(status().isCreated());
        indexarTodo();
        mvc.perform(comoAgente(get("/interna/articulos").param("consulta", "llamar al 8888-1234 por la contraseña")))
                .andExpect(status().isOk());

        assertThat(proveedor.recibidos()).isNotEmpty().allSatisfy(texto -> {
            assertThat(Enmascarador.detectar(texto)).isEmpty();
            assertThat(texto).doesNotContain("1-2345-0678", "ana@banco.cr", "8888-1234");
        });
    }

    @Test
    void la_api_interna_solo_acepta_la_cuenta_del_agente_y_solo_lectura() throws Exception {
        mvc.perform(get("/interna/articulos").param("consulta", "vpn")).andExpect(status().isUnauthorized());
        // Una persona, aunque sea de soporte, no entra a la API interna.
        mvc.perform(get("/interna/articulos").param("consulta", "vpn").with(httpBasic("tecnico1", CLAVE_USUARIOS)))
                .andExpect(r -> assertThat(r.getResponse().getStatus()).isIn(401, 403));
        // Y la cuenta del agente no sirve en la API de personas.
        mvc.perform(get("/api/tickets").with(httpBasic("agente", CLAVE_API_INTERNA))).andExpect(status().isUnauthorized());
        // Solo lectura.
        mvc.perform(comoAgente(post("/interna/articulos").param("consulta", "vpn"))).andExpect(status().isForbidden());
        mvc.perform(comoAgente(get("/interna/articulos").param("consulta", "vpn").param("limite", "500")))
                .andExpect(status().isBadRequest());
    }
}
