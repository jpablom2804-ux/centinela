package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.tickets.api.Canal;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import cr.centinela.mesaservicio.tickets.api.Prioridad;
import cr.centinela.mesaservicio.tickets.api.TipoTicket;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;
import java.time.Instant;
import java.util.List;
import java.util.UUID;

/**
 * Un DTO por acción (A6): cada uno trae solo lo que esa acción puede cambiar. Un campo de más (por ejemplo
 * "prioridad" al crear) se rechaza con 400. Los largos máximos (A4) son los mismos de docs/EVENTOS.md.
 */
final class Dtos {

    static final int MAX_TITULO = 200;
    static final int MAX_DESCRIPCION = 5000;
    static final int MAX_JUSTIFICACION = 1000;

    private Dtos() {}

    record CrearTicket(
            @NotNull TipoTicket tipo,
            @NotBlank @Size(max = MAX_TITULO) String titulo,
            @NotBlank @Size(max = MAX_DESCRIPCION) String descripcion,
            @Size(max = 100) String sistemaReportado,
            @NotNull Canal canal) {}

    record CambiarEstado(@NotNull EstadoTicket estado, @Size(max = MAX_JUSTIFICACION) String justificacion) {}

    record CambiarPrioridad(@NotNull Prioridad prioridad, @NotBlank @Size(max = MAX_JUSTIFICACION) String justificacion) {}

    record Asignar(@NotBlank @Size(max = 100) String tecnico) {}

    record Comentar(@NotBlank @Size(max = MAX_DESCRIPCION) String texto) {}

    record TicketVista(UUID id, TipoTicket tipo, String titulo, String descripcion, String sistemaReportado, Canal canal,
            EstadoTicket estado, Prioridad prioridad, String solicitante, String asignadoA, Instant creadoEn, Instant actualizadoEn) {

        static TicketVista de(Ticket t) {
            return new TicketVista(t.id(), t.tipo(), t.titulo(), t.descripcion(), t.sistemaReportado(), t.canal(), t.estado(),
                    t.prioridad(), t.solicitante(), t.asignadoA(), t.creadoEn(), t.actualizadoEn());
        }
    }

    record ComentarioVista(UUID id, String autor, String texto, Instant creadoEn) {

        static ComentarioVista de(Comentario c) {
            return new ComentarioVista(c.id(), c.autor(), c.texto(), c.creadoEn());
        }
    }

    record Pagina<T>(List<T> contenido, int pagina, int tamano, long total) {}
}
