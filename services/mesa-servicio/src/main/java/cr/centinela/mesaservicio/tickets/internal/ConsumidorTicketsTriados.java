package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.plataforma.Correlacion;
import java.time.Duration;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;
import org.springframework.amqp.AmqpRejectAndDontRequeueException;
import org.springframework.amqp.core.Message;
import org.springframework.amqp.rabbit.annotation.RabbitListener;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.context.annotation.Profile;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.dao.TransientDataAccessException;
import org.springframework.stereotype.Component;
import org.springframework.transaction.CannotCreateTransactionException;
import tools.jackson.core.JacksonException;
import tools.jackson.databind.ObjectMapper;

/**
 * Consume mesa-servicio.tickets-triados (A3: el usuario mesa_servicio solo puede leer esta cola).
 * <ul>
 *   <li>Lo que no cumple el contrato va a la DLQ, sin reintentar.</li>
 *   <li>Idempotente: una sola sugerencia por ticket; un evento repetido (o republicado por el agente) se descarta.</li>
 *   <li>Fallas transitorias de la base: reintentos con backoff y un máximo; después, a la DLQ.</li>
 * </ul>
 */
@Component
@Profile("!migracion")
class ConsumidorTicketsTriados {

    private static final Logger log = LoggerFactory.getLogger(ConsumidorTicketsTriados.class);

    private final ServicioSugerencias servicio;
    private final ObjectMapper json;
    private final int maxReintentos;
    private final Duration backoffBase;

    ConsumidorTicketsTriados(ServicioSugerencias servicio, ObjectMapper json,
            @Value("${centinela.mensajeria.consumo.max-reintentos}") int maxReintentos,
            @Value("${centinela.mensajeria.consumo.backoff-base}") Duration backoffBase) {
        this.servicio = servicio;
        this.json = json;
        this.maxReintentos = maxReintentos;
        this.backoffBase = backoffBase;
    }

    // [FLUJO TRIAJE_ASINCRONO 8] Recibe TicketTriado con el correlationId del ticket → ServicioSugerencias.registrar (paso 9); ack al volver
    @RabbitListener(queues = "${centinela.mensajeria.cola-triajes}")
    void recibir(Message mensaje) {
        var propiedades = mensaje.getMessageProperties();
        MDC.put(Correlacion.CLAVE_MDC, Correlacion.desdeCabecera(propiedades.getCorrelationId()));
        try {
            TicketTriadoV1 triado;
            try {
                triado = json.readValue(mensaje.getBody(), TicketTriadoV1.class);
            } catch (JacksonException e) {
                log.warn("Mensaje {} a la DLQ: JSON inválido o con campos fuera del contrato", propiedades.getMessageId());
                throw new AmqpRejectAndDontRequeueException("JSON inválido");
            }
            procesar(triado);
        } finally {
            MDC.remove(Correlacion.CLAVE_MDC);
        }
    }

    private void procesar(TicketTriadoV1 triado) {
        for (var intento = 0; ; intento++) {
            try {
                var resultado = servicio.registrar(triado);
                log.info("TicketTriado {} del ticket {}: {}", triado.id(), triado.datos().ticketId(), resultado);
                return;
            } catch (ServicioSugerencias.MensajeInvalido e) {
                log.warn("TicketTriado {} a la DLQ: {}", triado.id(), e.getMessage());
                throw new AmqpRejectAndDontRequeueException(e.getMessage());
            } catch (DataIntegrityViolationException e) {
                // Otra réplica guardó la sugerencia del mismo ticket al mismo tiempo.
                log.info("TicketTriado {}: duplicado (otra réplica lo guardó primero)", triado.id());
                return;
            } catch (TransientDataAccessException | CannotCreateTransactionException e) {
                if (intento >= maxReintentos) {
                    log.error("TicketTriado {} a la DLQ tras {} reintentos: {}", triado.id(), maxReintentos, e.getClass().getSimpleName());
                    throw new AmqpRejectAndDontRequeueException("base de datos no disponible");
                }
                esperar(backoffBase.multipliedBy(1L << intento));
            }
        }
    }

    private static void esperar(Duration espera) {
        try {
            Thread.sleep(espera);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new AmqpRejectAndDontRequeueException("interrumpido");
        }
    }
}
