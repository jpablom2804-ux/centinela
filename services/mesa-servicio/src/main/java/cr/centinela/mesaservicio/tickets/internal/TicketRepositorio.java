package cr.centinela.mesaservicio.tickets.internal;

import java.util.Optional;
import java.util.UUID;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;

interface TicketRepositorio extends JpaRepository<Ticket, UUID> {

    /** Para un solicitante: la misma consulta encuentra su ticket o nada, sin distinguir ajeno de inexistente. */
    Optional<Ticket> findByIdAndSolicitante(UUID id, String solicitante);

    Page<Ticket> findBySolicitante(String solicitante, Pageable pagina);

    /** A5: tickets creados por el usuario desde un instante (usa el índice por solicitante y fecha). */
    long countBySolicitanteAndCreadoEnAfter(String solicitante, java.time.Instant desde);
}
