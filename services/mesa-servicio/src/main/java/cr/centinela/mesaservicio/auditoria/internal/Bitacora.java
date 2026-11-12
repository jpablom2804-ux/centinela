package cr.centinela.mesaservicio.auditoria.internal;

import jakarta.persistence.EntityManager;
import java.time.Clock;
import java.time.temporal.ChronoUnit;
import org.springframework.stereotype.Component;

/** Inserta y verifica la bitácora. Solo INSERT y SELECT: el rol de la app no tiene otros permisos sobre ella. */
@Component
public class Bitacora {

    // Clave fija del bloqueo que serializa las inserciones: dos réplicas no pueden encadenar sobre el mismo hash.
    private static final long CLAVE_BLOQUEO = 7_302_141_001L;

    private final EntityManager em;
    private final Clock reloj;

    Bitacora(EntityManager em, Clock reloj) {
        this.em = em;
        this.reloj = reloj;
    }

    public void agregar(String actor, String accion, String recursoTipo, String recursoId, String antes, String despues) {
        em.createNativeQuery("select pg_advisory_xact_lock(:clave)").setParameter("clave", CLAVE_BLOQUEO).getSingleResult();
        var anterior = em.createQuery("select e.hash from EntradaBitacora e order by e.id desc", String.class)
                .setMaxResults(1)
                .getResultStream()
                .findFirst()
                .orElse(CadenaHash.INICIO);
        // PostgreSQL guarda microsegundos: se trunca antes de calcular el hash para que la verificación coincida.
        var ahora = reloj.instant().truncatedTo(ChronoUnit.MICROS);
        em.persist(new EntradaBitacora(ahora, actor, accion, recursoTipo, recursoId, antes, despues, anterior));
    }

    public Long primerRegistroAlterado() {
        var esperado = CadenaHash.INICIO;
        var registros = em.createQuery("select e from EntradaBitacora e order by e.id", EntradaBitacora.class)
                .getResultStream()
                .iterator();
        while (registros.hasNext()) {
            var e = registros.next();
            if (!esperado.equals(e.hashAnterior()) || !CadenaHash.calcular(e).equals(e.hash())) {
                return e.id();
            }
            esperado = e.hash();
        }
        return null;
    }
}
