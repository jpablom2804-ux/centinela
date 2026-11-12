package cr.centinela.mesaservicio.sla.internal;

import cr.centinela.mesaservicio.tickets.api.Prioridad;
import java.time.DayOfWeek;
import java.time.Duration;
import java.time.LocalDate;
import java.time.LocalTime;
import java.time.ZoneId;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * Políticas de SLA por prioridad y calendario laboral, desde configuración (centinela.sla). El tiempo de
 * resolución es hábil: corre solo dentro de la jornada.
 */
@ConfigurationProperties("centinela.sla")
record PoliticasSla(Map<Prioridad, Duration> resolucion, double umbralAlerta, Jornada jornada) {

    PoliticasSla {
        for (var p : Prioridad.values()) {
            if (!resolucion.containsKey(p)) {
                throw new IllegalArgumentException("Falta el tiempo de resolución de " + p + " en centinela.sla.resolucion");
            }
        }
        if (umbralAlerta <= 0 || umbralAlerta >= 1) {
            throw new IllegalArgumentException("centinela.sla.umbral-alerta debe estar entre 0 y 1");
        }
    }

    record Jornada(ZoneId zona, LocalTime inicio, LocalTime fin, List<DayOfWeek> dias, List<LocalDate> feriados) {}

    Duration objetivo(Prioridad prioridad) {
        return resolucion.get(prioridad);
    }

    CalendarioLaboral calendario() {
        return new CalendarioLaboral(jornada.zona(), jornada.inicio(), jornada.fin(), Set.copyOf(jornada.dias()),
                Set.copyOf(jornada.feriados() == null ? List.of() : jornada.feriados()));
    }
}
