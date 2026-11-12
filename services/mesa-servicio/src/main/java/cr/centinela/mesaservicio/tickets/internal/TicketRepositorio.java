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
}
