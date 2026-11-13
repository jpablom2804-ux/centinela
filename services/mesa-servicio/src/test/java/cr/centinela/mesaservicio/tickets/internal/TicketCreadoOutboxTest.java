package cr.centinela.mesaservicio.tickets.internal;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.httpBasic;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import cr.centinela.mesaservicio.PruebaIntegracion;
import cr.centinela.mesaservicio.plataforma.mensajeria.MensajeSaliente;
import cr.centinela.mesaservicio.plataforma.mensajeria.Outbox;
import cr.centinela.mesaservicio.plataforma.mensajeria.RelevoOutbox;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.amqp.core.AmqpAdmin;
import org.springframework.amqp.core.Binding;
import org.springframework.amqp.AmqpException;
import org.springframework.amqp.rabbit.connection.CachingConnectionFactory;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.core.Queue;
import org.springframework.amqp.rabbit.core.RabbitAdmin;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.transaction.IllegalTransactionStateException;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;

/** Ticket creado → outbox en la misma transacción → RabbitMQ real, con el texto ya enmascarado. */
@AutoConfigureMockMvc
class TicketCreadoOutboxTest extends PruebaIntegracion {

    /** La cola real del agente, declarada en deploy/rabbitmq (A3). La prueba la lee como administrador. */
    private static final String COLA = "agente.tickets-creados";
    private static final String USUARIO = "coordinador1";
    private static final CachingConnectionFactory CONEXION_ADMIN = conexionAdmin();
    private static final RabbitTemplate RABBIT_ADMIN = new RabbitTemplate(CONEXION_ADMIN);
    private static final RabbitAdmin ADMIN = new RabbitAdmin(CONEXION_ADMIN);
    private static final String TICKET_CON_DATOS = """
            {"tipo":"incidente","titulo":"Bloqueo de la cuenta de ana@banco.cr",\
            "descripcion":"Mi cédula es 1-2345-0678 y mi celular 8888-1234. La contraseña: Banco2026! no sirve.",\
            "sistemaReportado":"core","canal":"correo"}
            """;

    @Autowired MockMvc mvc;
    @Autowired ObjectMapper json;
    @Autowired JdbcTemplate jdbc;
    @Autowired RelevoOutbox relevo;
    @Autowired Outbox outbox;
    /** Los de la aplicación: conectados como mesa_servicio. */
    @Autowired AmqpAdmin adminDeLaApp;
    @Autowired RabbitTemplate rabbitDeLaApp;
    @Autowired RelojMovible reloj;

    private static CachingConnectionFactory conexionAdmin() {
        var conexion = new CachingConnectionFactory(RABBIT.getHost(), RABBIT.getAmqpPort());
        conexion.setUsername("centinela");
        conexion.setPassword(CLAVE_MQ_ADMIN);
        conexion.setVirtualHost("centinela");
        return conexion;
    }

    private String crear() throws Exception {
        var peticion = post("/api/tickets").content(TICKET_CON_DATOS).contentType(MediaType.APPLICATION_JSON)
                .with(httpBasic(USUARIO, CLAVE_USUARIOS));
        var cuerpo = mvc.perform(peticion).andExpect(status().isCreated()).andReturn().getResponse().getContentAsString();
        return json.readTree(cuerpo).get("id").asString();
    }

    private void conColaEnlazada() {
        ADMIN.declareBinding(ENLACE);
        ADMIN.purgeQueue(COLA, false);
    }

    private static final Binding ENLACE =
            new Binding(COLA, Binding.DestinationType.QUEUE, PublicadorTicketCreado.EXCHANGE, PublicadorTicketCreado.RUTA, null);

    private void publicarTodo() {
        while (relevo.publicarPendientes() > 0) {
            // Las otras pruebas también dejan tickets en el outbox: se vacía todo lo pendiente.
        }
    }

    private Message recibirDe(String ticketId) {
        for (var i = 0; i < 500; i++) {
            var mensaje = RABBIT_ADMIN.receive(COLA, 2000);
            assertThat(mensaje).as("no llegó el TicketCreado del ticket %s", ticketId).isNotNull();
            if (sobre(mensaje).get("datos").get("ticketId").asString().equals(ticketId)) {
                return mensaje;
            }
        }
        throw new AssertionError("Demasiados mensajes en la cola de prueba");
    }

    private JsonNode sobre(Message mensaje) {
        return json.readTree(new String(mensaje.getBody(), StandardCharsets.UTF_8));
    }

    @Test
    void publica_ticket_creado_enmascarado_con_su_sobre() throws Exception {
        conColaEnlazada();
        var id = crear();

        publicarTodo();

        var mensaje = recibirDe(id);
        var sobre = sobre(mensaje);
        var datos = sobre.get("datos");
        assertThat(sobre.get("tipo").asString()).isEqualTo("TicketCreado");
        assertThat(sobre.get("version").asInt()).isEqualTo(1);
        assertThat(sobre.get("ocurridoEn").asString()).endsWith("Z");
        assertThat(datos.get("titulo").asString()).isEqualTo("Bloqueo de la cuenta de [CORREO]");
        assertThat(datos.get("descripcion").asString())
                .isEqualTo("Mi cédula es [CEDULA] y mi celular [TELEFONO]. La [CREDENCIAL] no sirve.");
        assertThat(datos.get("tipo").asString()).isEqualTo("incidente");
        assertThat(datos.get("canal").asString()).isEqualTo("correo");
        assertThat(mensaje.getMessageProperties().getMessageId()).isEqualTo(sobre.get("id").asString());
        assertThat(mensaje.getMessageProperties().getType()).isEqualTo("TicketCreado");
        assertThat(jdbc.queryForObject("select publicado_en is not null from mesa_servicio.outbox where id = ?::uuid",
                Boolean.class, sobre.get("id").asString())).isTrue();

        // El ticket guardado no cambia: el técnico ve lo que escribió el usuario.
        mvc.perform(get("/api/tickets/" + id).with(httpBasic(USUARIO, CLAVE_USUARIOS)))
                .andExpect(jsonPath("$.titulo").value("Bloqueo de la cuenta de ana@banco.cr"));
    }

    @Test
    void el_id_de_correlacion_viaja_desde_la_peticion_hasta_el_mensaje() throws Exception {
        conColaEnlazada();
        var peticion = post("/api/tickets").content(TICKET_CON_DATOS).contentType(MediaType.APPLICATION_JSON)
                .with(httpBasic(USUARIO, CLAVE_USUARIOS)).header("X-Request-ID", "a1b2c3d4e5f60718293a4b5c6d7e8f90");
        mvc.perform(peticion).andExpect(status().isCreated()).andExpect(header().string("X-Request-ID", "a1b2c3d4e5f60718293a4b5c6d7e8f90"));
        var idDelOutbox = jdbc.queryForObject(
                "select cuerpo::json->'datos'->>'ticketId' from mesa_servicio.outbox where correlation_id = ?",
                String.class, "a1b2c3d4e5f60718293a4b5c6d7e8f90");

        publicarTodo();

        var mensaje = recibirDe(idDelOutbox);
        assertThat(sobre(mensaje).get("correlationId").asString()).isEqualTo("a1b2c3d4e5f60718293a4b5c6d7e8f90");
        assertThat(mensaje.getMessageProperties().getCorrelationId()).isEqualTo("a1b2c3d4e5f60718293a4b5c6d7e8f90");
    }

    @Test
    void un_x_request_id_invalido_se_reemplaza() throws Exception {
        var peticion = post("/api/tickets").content(TICKET_CON_DATOS).contentType(MediaType.APPLICATION_JSON)
                .with(httpBasic(USUARIO, CLAVE_USUARIOS)).header("X-Request-ID", "<script>alert(1)</script>");

        var respuesta = mvc.perform(peticion).andExpect(status().isCreated()).andReturn().getResponse();

        assertThat(respuesta.getHeader("X-Request-ID")).matches("[0-9a-f-]{36}");
    }

    @Test
    void sin_cola_no_se_da_por_publicado_y_se_reintenta_con_espera() throws Exception {
        ADMIN.removeBinding(ENLACE);
        String evento;
        try {
            var id = crear();
            evento = jdbc.queryForObject("select id::text from mesa_servicio.outbox where cuerpo::json->'datos'->>'ticketId' = ?",
                    String.class, id);
            relevo.publicarPendientes();
        } finally {
            ADMIN.declareBinding(ENLACE);
        }

        var fila = jdbc.queryForMap("select publicado_en, intentos, ultimo_error, proximo_intento from mesa_servicio.outbox where id = ?::uuid",
                evento);
        assertThat(fila.get("publicado_en")).isNull();
        assertThat(fila.get("intentos")).isEqualTo(1);
        assertThat(fila.get("ultimo_error")).isEqualTo("SIN_COLA");
        assertThat(((java.sql.Timestamp) fila.get("proximo_intento")).toInstant()).isAfter(reloj.instant());
    }

    @Test
    void mesa_servicio_solo_puede_publicar_en_su_exchange() {
        // A3: con su usuario no lee la cola del agente, no declara ni borra colas y no publica en triajes.
        assertThatThrownBy(() -> rabbitDeLaApp.receive(COLA)).isInstanceOf(AmqpException.class);
        assertThatThrownBy(() -> adminDeLaApp.declareQueue(new Queue("intrusa", true))).isInstanceOf(AmqpException.class);
        assertThatThrownBy(() -> adminDeLaApp.purgeQueue(COLA, false)).isInstanceOf(AmqpException.class);
        assertThatThrownBy(() -> rabbitDeLaApp.invoke(operaciones -> {
            operaciones.convertAndSend("triajes", "ticket.triado", "{}");
            operaciones.waitForConfirmsOrDie(5000);
            return null;
        })).hasStackTraceContaining("ACCESS_REFUSED");
    }

    @Test
    void registrar_fuera_de_una_transaccion_falla() {
        var mensaje = new MensajeSaliente("tickets", "ticket.creado", "Prueba", 1, Instant.now(), Map.of());

        assertThatThrownBy(() -> outbox.registrar(mensaje)).isInstanceOf(IllegalTransactionStateException.class);
    }
}
