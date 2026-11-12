package cr.centinela.mesaservicio.tickets.internal;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * A5: límite de tickets por usuario. El rate limiting de nginx es por IP, y en un banco todos salen por el
 * mismo proxy: hace falta además un límite por persona. Se cuenta en la base, así vale igual con varias
 * réplicas y no guarda estado en memoria.
 */
@ConfigurationProperties("centinela.tickets")
record LimitesTickets(int maxPorHora) {

    LimitesTickets {
        if (maxPorHora < 1) {
            throw new IllegalArgumentException("centinela.tickets.max-por-hora debe ser al menos 1");
        }
    }
}
