package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.conocimiento.api.IndiceSemantico;
import cr.centinela.mesaservicio.plataforma.Enmascarador;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import cr.centinela.mesaservicio.tickets.api.TipoTicket;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;
import java.time.Instant;
import java.util.EnumSet;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.data.domain.PageRequest;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * API interna de solo lectura para las herramientas del agente (buscar_tickets_similares y
 * consultar_estado_servicio). Solo la cuenta de servicio del agente, solo GET, nunca expuesta por nginx, y todo
 * lo que devuelve va enmascarado: el texto de un ticket lo escribió una persona.
 *
 * <p>El ticketId de la ruta lo pone el ciclo del agente (el ticket que está triando), nunca el modelo; aquí solo
 * sirve para no devolver el mismo ticket como "similar".
 */
@RestController
@RequestMapping("/interna")
class ApiInternaTickets {

    private static final int MAX_SOLUCION = 500;
    private static final EnumSet<EstadoTicket> TERMINADOS = EnumSet.of(EstadoTicket.RESUELTO, EstadoTicket.CERRADO);

    private final IndiceSemantico indice;
    private final TicketRepositorio tickets;
    private final ComentarioRepositorio comentarios;

    ApiInternaTickets(IndiceSemantico indice, TicketRepositorio tickets, ComentarioRepositorio comentarios) {
        this.indice = indice;
        this.tickets = tickets;
        this.comentarios = comentarios;
    }

    record TicketSimilar(UUID id, String titulo, String estado, String sistema, String solucion, double similitud, String metodo) {}

    record IncidenteAbierto(UUID id, String titulo, String estado, Instant desde) {}

    // [FLUJO HERRAMIENTAS_AGENTE 2a] Busca tickets parecidos (sin el ticket en curso) y los devuelve enmascarados → agente
    @GetMapping("/tickets/{ticketId}/similares")
    @Transactional(readOnly = true)
    List<TicketSimilar> similares(@PathVariable UUID ticketId, @RequestParam @NotBlank @Size(max = 300) String consulta,
            @RequestParam(defaultValue = "5") @Min(1) @Max(10) int limite) {
        var coincidencias = indice.buscar(IndiceSemantico.TipoRecurso.TICKET, consulta, ticketId.toString(), limite);
        var ids = coincidencias.stream().map(c -> UUID.fromString(c.id())).toList();
        Map<UUID, Ticket> encontrados = tickets.findAllById(ids).stream().collect(Collectors.toMap(Ticket::id, Function.identity()));
        return coincidencias.stream()
                .filter(c -> encontrados.containsKey(UUID.fromString(c.id())))
                .map(c -> {
                    var t = encontrados.get(UUID.fromString(c.id()));
                    return new TicketSimilar(t.id(), Enmascarador.enmascarar(t.titulo()), t.estado().codigo(),
                            Enmascarador.enmascarar(t.sistemaReportado()), solucion(t), Math.round(c.similitud() * 100) / 100.0,
                            c.metodo().name().toLowerCase());
                })
                .toList();
    }

    // [FLUJO HERRAMIENTAS_AGENTE 2c] Incidentes abiertos del sistema (¿hay una caída general?), enmascarados → agente
    @GetMapping("/sistemas/{sistema}/incidentes")
    @Transactional(readOnly = true)
    List<IncidenteAbierto> incidentes(@PathVariable @Pattern(regexp = "^[a-z_]{2,50}$") String sistema,
            @RequestParam(defaultValue = "5") @Min(1) @Max(10) int limite) {
        return tickets.findByTipoAndSistemaReportadoAndEstadoNotInOrderByCreadoEnDesc(TipoTicket.INCIDENTE, sistema, TERMINADOS,
                        PageRequest.of(0, limite))
                .stream()
                .map(t -> new IncidenteAbierto(t.id(), Enmascarador.enmascarar(t.titulo()), t.estado().codigo(), t.creadoEn()))
                .toList();
    }

    /** Para un ticket ya resuelto, la última respuesta de soporte (no del solicitante) sirve como solución conocida. */
    private String solucion(Ticket ticket) {
        if (!TERMINADOS.contains(ticket.estado())) {
            return null;
        }
        return comentarios.findByTicketIdOrderByCreadoEnAsc(ticket.id()).reversed().stream()
                .filter(c -> !c.autor().equals(ticket.solicitante()))
                .findFirst()
                .map(c -> Enmascarador.enmascarar(c.texto()))
                .map(texto -> texto.length() > MAX_SOLUCION ? texto.substring(0, MAX_SOLUCION) + "…" : texto)
                .orElse(null);
    }
}
