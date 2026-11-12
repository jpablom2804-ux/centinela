package cr.centinela.mesaservicio.plataforma.mensajeria;

import cr.centinela.mesaservicio.plataforma.Correlacion;
import java.nio.charset.StandardCharsets;
import java.time.Clock;
import java.time.Duration;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;
import org.springframework.amqp.AmqpException;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.core.MessageDeliveryMode;
import org.springframework.amqp.core.MessageProperties;
import org.springframework.amqp.core.MessagePropertiesBuilder;
import org.springframework.amqp.rabbit.connection.CorrelationData;
import org.springframework.amqp.rabbit.core.RabbitTemplate;
import org.springframework.context.annotation.Profile;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Publica en RabbitMQ lo que quedó en el outbox. Aguanta varias réplicas sin ShedLock: cada una toma un lote
 * con {@code FOR UPDATE SKIP LOCKED}, así dos réplicas nunca toman el mismo evento a la vez.
 *
 * <p>Un evento se marca publicado solo con el ack del broker (publisher confirms) y si llegó a alguna cola
 * ({@code mandatory}): un mensaje sin cola se perdería en silencio. Puede publicarse dos veces (por ejemplo,
 * si la réplica cae entre el ack y el commit); por eso los consumidores son idempotentes por {@code id}.
 */
@Component
@Profile("!migracion")
public class RelevoOutbox {

    private static final Logger log = LoggerFactory.getLogger(RelevoOutbox.class);

    private static final String TOMAR_PENDIENTES = """
            SELECT id, exchange, routing_key, tipo, correlation_id, cuerpo, intentos
              FROM mesa_servicio.outbox
             WHERE publicado_en IS NULL AND intentos < ? AND proximo_intento <= ?
             ORDER BY creado_en
             LIMIT ?
               FOR UPDATE SKIP LOCKED
            """;
    private static final String MARCAR_PUBLICADO = "UPDATE mesa_servicio.outbox SET publicado_en = ? WHERE id = ?";
    private static final String MARCAR_FALLO = """
            UPDATE mesa_servicio.outbox SET intentos = intentos + 1, proximo_intento = ?, ultimo_error = ? WHERE id = ?
            """;

    private final JdbcTemplate jdbc;
    private final RabbitTemplate rabbit;
    private final TransactionTemplate transaccion;
    private final Clock reloj;
    private final PropiedadesMensajeria propiedades;

    RelevoOutbox(JdbcTemplate jdbc, RabbitTemplate rabbit, TransactionTemplate transaccion, Clock reloj,
            PropiedadesMensajeria propiedades) {
        this.jdbc = jdbc;
        this.rabbit = rabbit;
        this.transaccion = transaccion;
        this.reloj = reloj;
        this.propiedades = propiedades;
    }

    @Scheduled(fixedDelayString = "${centinela.mensajeria.relevo}", initialDelayString = "${centinela.mensajeria.relevo}")
    void programado() {
        publicarPendientes();
    }

    // [FLUJO TRIAJE_ASINCRONO 3] Toma un lote con SKIP LOCKED, publica con confirmación del broker y marca publicado; si falla, backoff → agente (paso 4, fase 3)
    /** @return cuántos eventos se publicaron en esta vuelta */
    public int publicarPendientes() {
        Integer publicados = transaccion.execute(estado -> {
            var ahora = reloj.instant();
            List<Pendiente> pendientes = jdbc.query(TOMAR_PENDIENTES, (fila, n) -> new Pendiente(
                    fila.getObject("id", UUID.class), fila.getString("exchange"), fila.getString("routing_key"),
                    fila.getString("tipo"), fila.getString("correlation_id"), fila.getString("cuerpo"), fila.getInt("intentos")),
                    propiedades.maxIntentos(), Outbox.utc(ahora), propiedades.lote());
            var cuenta = 0;
            for (var pendiente : pendientes) {
                var error = publicar(pendiente);
                if (error.isEmpty()) {
                    jdbc.update(MARCAR_PUBLICADO, Outbox.utc(reloj.instant()), pendiente.id());
                    cuenta++;
                } else {
                    registrarFallo(pendiente, error.get());
                }
            }
            return cuenta;
        });
        return publicados == null ? 0 : publicados;
    }

    /** @return vacío si el broker confirmó y el mensaje llegó a una cola; si no, un código de error sin datos */
    private Optional<String> publicar(Pendiente pendiente) {
        var propiedadesMensaje = MessagePropertiesBuilder.newInstance()
                .setContentType(MessageProperties.CONTENT_TYPE_JSON)
                .setContentEncoding(StandardCharsets.UTF_8.name())
                .setMessageId(pendiente.id().toString())
                .setType(pendiente.tipo())
                .setCorrelationId(pendiente.correlationId())
                .setDeliveryMode(MessageDeliveryMode.PERSISTENT)
                .build();
        var mensaje = new Message(pendiente.cuerpo().getBytes(StandardCharsets.UTF_8), propiedadesMensaje);
        var confirmacion = new CorrelationData(pendiente.id().toString());
        MDC.put(Correlacion.CLAVE_MDC, pendiente.correlationId());
        try {
            rabbit.send(pendiente.exchange(), pendiente.routingKey(), mensaje, confirmacion);
            var ack = confirmacion.getFuture().get(propiedades.esperaConfirmacion().toMillis(), TimeUnit.MILLISECONDS);
            // Con mandatory, el mensaje devuelto se registra antes de completar la confirmación.
            if (confirmacion.getReturned() != null) {
                return Optional.of("SIN_COLA");
            }
            return ack.ack() ? Optional.empty() : Optional.of("RECHAZADO_POR_BROKER");
        } catch (AmqpException e) {
            return Optional.of("BROKER_NO_DISPONIBLE");
        } catch (TimeoutException e) {
            return Optional.of("SIN_CONFIRMACION");
        } catch (ExecutionException e) {
            return Optional.of("ERROR_DE_CONFIRMACION");
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return Optional.of("INTERRUMPIDO");
        } finally {
            MDC.remove(Correlacion.CLAVE_MDC);
        }
    }

    private void registrarFallo(Pendiente pendiente, String error) {
        var intentos = pendiente.intentos() + 1;
        var espera = esperaTras(intentos);
        jdbc.update(MARCAR_FALLO, Outbox.utc(reloj.instant().plus(espera)), error, pendiente.id());
        if (intentos >= propiedades.maxIntentos()) {
            // Equivale a la DLQ del lado del productor: queda en la tabla para revisarlo, sin reintentar más.
            log.error("Evento {} ({}) agotó sus {} intentos de publicación: {}", pendiente.id(), pendiente.tipo(), intentos, error);
        } else {
            log.warn("Evento {} ({}) sin publicar ({}); intento {} de {}, reintento en {}", pendiente.id(), pendiente.tipo(),
                    error, intentos, propiedades.maxIntentos(), espera);
        }
    }

    /** Backoff exponencial: 2, 4, 8… segundos, con tope. */
    private Duration esperaTras(int intentos) {
        var segundos = 1L << Math.min(intentos, 20);
        var espera = Duration.ofSeconds(segundos);
        return espera.compareTo(propiedades.maxEspera()) > 0 ? propiedades.maxEspera() : espera;
    }

    private record Pendiente(UUID id, String exchange, String routingKey, String tipo, String correlationId, String cuerpo,
            int intentos) {}
}
