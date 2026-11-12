package cr.centinela.mesaservicio.plataforma.mensajeria;

import java.time.Instant;
import java.util.Map;
import java.util.Objects;

/**
 * Un evento para otro servicio (docs/EVENTOS.md). Los datos ya deben venir enmascarados: el outbox no sabe
 * qué campo es texto libre.
 */
public record MensajeSaliente(String exchange, String routingKey, String tipo, int version, Instant ocurridoEn,
        Map<String, ?> datos) {

    public MensajeSaliente {
        Objects.requireNonNull(exchange);
        Objects.requireNonNull(routingKey);
        Objects.requireNonNull(tipo);
        Objects.requireNonNull(ocurridoEn);
        Objects.requireNonNull(datos);
        if (version < 1) {
            throw new IllegalArgumentException("La versión del contrato empieza en 1");
        }
    }
}
