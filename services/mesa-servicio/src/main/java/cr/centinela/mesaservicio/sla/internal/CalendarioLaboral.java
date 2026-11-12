package cr.centinela.mesaservicio.sla.internal;

import java.time.DayOfWeek;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalTime;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.util.Objects;
import java.util.Set;

/**
 * Tiempo hábil entre dos instantes. Los instantes son UTC; la jornada se define en la zona de la entidad
 * (por ejemplo America/Costa_Rica). Fuera de horario, fines de semana y feriados el SLA no corre.
 */
record CalendarioLaboral(ZoneId zona, LocalTime inicio, LocalTime fin, Set<DayOfWeek> diasLaborales, Set<LocalDate> feriados) {

    CalendarioLaboral {
        Objects.requireNonNull(zona);
        if (!inicio.isBefore(fin)) {
            throw new IllegalArgumentException("La jornada debe empezar antes de terminar.");
        }
        if (diasLaborales.isEmpty()) {
            throw new IllegalArgumentException("Hace falta al menos un día laboral.");
        }
        diasLaborales = Set.copyOf(diasLaborales);
        feriados = Set.copyOf(feriados);
    }

    /** Tiempo hábil transcurrido entre {@code desde} y {@code hasta} (cero si hasta ≤ desde). */
    Duration tiempoHabil(Instant desde, Instant hasta) {
        if (!hasta.isAfter(desde)) {
            return Duration.ZERO;
        }
        var total = Duration.ZERO;
        var dia = desde.atZone(zona).toLocalDate();
        var ultimo = hasta.atZone(zona).toLocalDate();
        while (!dia.isAfter(ultimo)) {
            if (esLaboral(dia)) {
                var abre = dia.atTime(inicio).atZone(zona).toInstant();
                var cierra = dia.atTime(fin).atZone(zona).toInstant();
                var a = max(abre, desde);
                var b = min(cierra, hasta);
                if (b.isAfter(a)) {
                    total = total.plus(Duration.between(a, b));
                }
            }
            dia = dia.plusDays(1);
        }
        return total;
    }

    /** Instante en que se cumplen {@code habil} horas hábiles contadas desde {@code desde}. */
    Instant sumar(Instant desde, Duration habil) {
        var restante = habil;
        var cursor = desde.atZone(zona);
        while (true) {
            var dia = cursor.toLocalDate();
            if (esLaboral(dia)) {
                var abre = dia.atTime(inicio).atZone(zona);
                var cierra = dia.atTime(fin).atZone(zona);
                var desdeHoy = max(cursor, abre);
                if (desdeHoy.isBefore(cierra)) {
                    var disponible = Duration.between(desdeHoy, cierra);
                    if (disponible.compareTo(restante) >= 0) {
                        return desdeHoy.plus(restante).toInstant();
                    }
                    restante = restante.minus(disponible);
                }
            }
            cursor = dia.plusDays(1).atStartOfDay(zona);
        }
    }

    private boolean esLaboral(LocalDate dia) {
        return diasLaborales.contains(dia.getDayOfWeek()) && !feriados.contains(dia);
    }

    private static Instant max(Instant a, Instant b) {
        return a.isAfter(b) ? a : b;
    }

    private static Instant min(Instant a, Instant b) {
        return a.isBefore(b) ? a : b;
    }

    private static ZonedDateTime max(ZonedDateTime a, ZonedDateTime b) {
        return a.isAfter(b) ? a : b;
    }
}
