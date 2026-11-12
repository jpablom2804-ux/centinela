package cr.centinela.mesaservicio.tickets.internal;

import org.springframework.amqp.core.DirectExchange;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * El exchange es del productor. La cola y su DLQ las declara quien consume (el agente), así cada servicio
 * solo necesita permiso sobre lo suyo (A3).
 */
@Configuration
class TopologiaTickets {

    @Bean
    DirectExchange exchangeTickets() {
        return new DirectExchange(PublicadorTicketCreado.EXCHANGE, true, false);
    }
}
