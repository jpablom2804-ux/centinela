package cr.centinela.mesaservicio.sla.internal;

import cr.centinela.mesaservicio.tickets.api.Prioridad;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import java.time.Duration;
import java.time.Instant;
import java.util.UUID;

/**
 * Reloj de resolución de un ticket. Acumula tiempo hábil mientras corre y se detiene en pausa
 * (en_espera) o al resolverse. Es del módulo sla: guarda el id del ticket, no lee su tabla.
 */
@Entity
@Table(schema = "mesa_servicio", name = "sla_reloj")
class RelojSla {

    @Id
    private UUID ticketId;

    @Enumerated(EnumType.STRING)
    private Prioridad prioridad;

    /** Tiempo hábil acumulado hasta {@code ultimoCorte}, en milisegundos. */
    private long consumidoMs;

    private boolean corriendo;
    private Instant ultimoCorte;
    private boolean alertaEnviada;
    private boolean vencido;

    @Version
    private Long version;

    protected RelojSla() {}

    static RelojSla iniciar(UUID ticketId, Prioridad prioridad, Instant ahora) {
        var r = new RelojSla();
        r.ticketId = ticketId;
        r.prioridad = prioridad;
        r.corriendo = true;
        r.ultimoCorte = ahora;
        return r;
    }

    Duration consumido(Instant ahora, CalendarioLaboral calendario) {
        var acumulado = Duration.ofMillis(consumidoMs);
        return corriendo ? acumulado.plus(calendario.tiempoHabil(ultimoCorte, ahora)) : acumulado;
    }

    /** Acumula lo corrido hasta ahora y se detiene (pausa o resolución). */
    void detener(Instant ahora, CalendarioLaboral calendario) {
        if (corriendo) {
            consumidoMs = consumido(ahora, calendario).toMillis();
            corriendo = false;
            ultimoCorte = ahora;
        }
    }

    void reanudar(Instant ahora) {
        if (!corriendo) {
            corriendo = true;
            ultimoCorte = ahora;
        }
    }

    /** Al cambiar la prioridad cambia el objetivo: las alertas se vuelven a evaluar contra el nuevo. */
    void cambiarPrioridad(Prioridad nueva) {
        prioridad = nueva;
        alertaEnviada = false;
        vencido = false;
    }

    void marcarAlerta() {
        alertaEnviada = true;
    }

    void marcarVencido() {
        alertaEnviada = true;
        vencido = true;
    }

    UUID ticketId() { return ticketId; }
    Prioridad prioridad() { return prioridad; }
    boolean corriendo() { return corriendo; }
    boolean alertaEnviada() { return alertaEnviada; }
    boolean vencido() { return vencido; }
}
