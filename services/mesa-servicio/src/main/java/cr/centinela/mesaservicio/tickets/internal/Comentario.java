package cr.centinela.mesaservicio.tickets.internal;

import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.UUID;
import org.hibernate.annotations.Immutable;

/** Comentario de un ticket. No se edita ni se borra: lo dicho queda dicho (y auditado). */
@Entity
@Immutable
@Table(schema = "mesa_servicio", name = "comentario")
class Comentario {

    @Id
    private UUID id;

    private UUID ticketId;
    private String autor;
    private String texto;
    private Instant creadoEn;

    protected Comentario() {}

    Comentario(UUID ticketId, String autor, String texto, Instant creadoEn) {
        this.id = UUID.randomUUID();
        this.ticketId = ticketId;
        this.autor = autor;
        this.texto = texto;
        this.creadoEn = creadoEn;
    }

    UUID id() { return id; }
    String autor() { return autor; }
    String texto() { return texto; }
    Instant creadoEn() { return creadoEn; }
}
