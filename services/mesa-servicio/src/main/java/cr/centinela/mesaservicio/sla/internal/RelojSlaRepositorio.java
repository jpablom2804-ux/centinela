package cr.centinela.mesaservicio.sla.internal;

import java.util.List;
import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

interface RelojSlaRepositorio extends JpaRepository<RelojSla, UUID> {

    /** Relojes que todavía pueden disparar una alerta: corriendo y sin vencer. */
    List<RelojSla> findByCorriendoTrueAndVencidoFalse();
}
