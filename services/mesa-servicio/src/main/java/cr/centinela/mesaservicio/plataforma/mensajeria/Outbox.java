package cr.centinela.mesaservicio.plataforma.mensajeria;

import cr.centinela.mesaservicio.plataforma.Correlacion;
import java.time.Clock;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.LinkedHashMap;
import java.util.UUID;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import tools.jackson.databind.ObjectMapper;

/**
 * Outbox transaccional: el evento se guarda en la misma transacción que el cambio que lo origina. Si la
 * transacción se revierte, el evento no existe; si se confirma, {@link RelevoOutbox} lo publica tarde o
 * temprano, aunque RabbitMQ esté caído en ese momento.
 */
@Component
public class Outbox {

    private static final String INSERTAR = """
            INSERT INTO mesa_servicio.outbox (id, exchange, routing_key, tipo, correlation_id, cuerpo, creado_en, proximo_intento)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """;

    private final JdbcTemplate jdbc;
    private final ObjectMapper json;
    private final Clock reloj;

    Outbox(JdbcTemplate jdbc, ObjectMapper json, Clock reloj) {
        this.jdbc = jdbc;
        this.json = json;
        this.reloj = reloj;
    }

    // [FLUJO TRIAJE_ASINCRONO 2] Arma el sobre (id, tipo, version, correlationId, ocurridoEn) y lo guarda sin publicar → RelevoOutbox (paso 3)
    /** MANDATORY: un evento fuera de la transacción de su cambio rompería la garantía del outbox. */
    @Transactional(propagation = Propagation.MANDATORY)
    public UUID registrar(MensajeSaliente mensaje) {
        var id = UUID.randomUUID();
        var correlacion = Correlacion.actual();
        var sobre = new LinkedHashMap<String, Object>();
        sobre.put("id", id.toString());
        sobre.put("tipo", mensaje.tipo());
        sobre.put("version", mensaje.version());
        sobre.put("correlationId", correlacion);
        sobre.put("ocurridoEn", DateTimeFormatter.ISO_INSTANT.format(mensaje.ocurridoEn().truncatedTo(ChronoUnit.MILLIS)));
        sobre.put("datos", mensaje.datos());
        var ahora = utc(reloj.instant());
        jdbc.update(INSERTAR, id, mensaje.exchange(), mensaje.routingKey(), mensaje.tipo(), correlacion,
                json.writeValueAsString(sobre), ahora, ahora);
        return id;
    }

    static OffsetDateTime utc(Instant instante) {
        return instante.atOffset(ZoneOffset.UTC);
    }
}
