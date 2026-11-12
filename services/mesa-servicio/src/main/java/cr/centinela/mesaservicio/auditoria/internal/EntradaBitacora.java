package cr.centinela.mesaservicio.auditoria.internal;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import org.hibernate.annotations.Immutable;

/** Registro de la bitácora. Inmutable también en la aplicación: Hibernate nunca genera un UPDATE. */
@Entity
@Immutable
@Table(schema = "auditoria", name = "bitacora")
public class EntradaBitacora {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    private Instant ocurridoEn;
    private String actor;
    private String accion;
    private String recursoTipo;
    private String recursoId;

    // Texto exacto (no jsonb): el hash se calcula sobre estos caracteres.
    private String antes;

    private String despues;

    @Column(name = "hash_anterior")
    private String hashAnterior;

    private String hash;

    protected EntradaBitacora() {}

    EntradaBitacora(Instant ocurridoEn, String actor, String accion, String recursoTipo, String recursoId,
            String antes, String despues, String hashAnterior) {
        this.ocurridoEn = ocurridoEn;
        this.actor = actor;
        this.accion = accion;
        this.recursoTipo = recursoTipo;
        this.recursoId = recursoId;
        this.antes = antes;
        this.despues = despues;
        this.hashAnterior = hashAnterior;
        this.hash = CadenaHash.calcular(this);
    }

    public Long id() {
        return id;
    }

    public String accion() {
        return accion;
    }

    public String recursoId() {
        return recursoId;
    }

    Instant ocurridoEn() {
        return ocurridoEn;
    }

    String actor() {
        return actor;
    }

    String recursoTipo() {
        return recursoTipo;
    }

    String antes() {
        return antes;
    }

    String despues() {
        return despues;
    }

    String hashAnterior() {
        return hashAnterior;
    }

    String hash() {
        return hash;
    }
}
