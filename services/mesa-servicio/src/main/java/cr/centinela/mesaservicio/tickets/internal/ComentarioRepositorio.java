package cr.centinela.mesaservicio.tickets.internal;

import java.util.List;
import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

interface ComentarioRepositorio extends JpaRepository<Comentario, UUID> {

    List<Comentario> findByTicketIdOrderByCreadoEnAsc(UUID ticketId);
}
