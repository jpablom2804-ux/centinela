package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.auditoria.EventoAuditoria;
import cr.centinela.mesaservicio.auditoria.RegistroAuditoria;
import cr.centinela.mesaservicio.plataforma.Errores;
import cr.centinela.mesaservicio.plataforma.Rol;
import cr.centinela.mesaservicio.plataforma.UsuarioActual;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import cr.centinela.mesaservicio.tickets.api.EventosTicket;
import java.time.Clock;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.domain.Sort;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * Reglas de los tickets: quién puede hacer qué, sobre qué ticket. Cada cambio queda en la bitácora y
 * publica su evento en memoria dentro de la misma transacción.
 */
@Service
class ServicioTickets {

    private static final int MAX_TAMANO_PAGINA = 100;
    private static final String RECURSO = "ticket";

    private final TicketRepositorio tickets;
    private final ComentarioRepositorio comentarios;
    private final RegistroAuditoria auditoria;
    private final ApplicationEventPublisher eventos;
    private final Clock reloj;
    private final LimitesTickets limites;
    private final PublicadorTicketCreado publicador;

    ServicioTickets(TicketRepositorio tickets, ComentarioRepositorio comentarios, RegistroAuditoria auditoria,
            ApplicationEventPublisher eventos, Clock reloj, LimitesTickets limites, PublicadorTicketCreado publicador) {
        this.tickets = tickets;
        this.comentarios = comentarios;
        this.auditoria = auditoria;
        this.eventos = eventos;
        this.reloj = reloj;
        this.limites = limites;
        this.publicador = publicador;
    }

    // [FLUJO CREAR_TICKET 2] Valida el rol y el límite por usuario (A5), crea el ticket en estado nuevo → paso 3 (bitácora) y 4 (evento)
    @Transactional
    Dtos.TicketVista crear(UsuarioActual usuario, Dtos.CrearTicket datos) {
        exigirAlguno(usuario, Rol.SOLICITANTE, Rol.TECNICO, Rol.COORDINADOR);
        var ahora = reloj.instant();
        if (tickets.countBySolicitanteAndCreadoEnAfter(usuario.id(), ahora.minus(java.time.Duration.ofHours(1))) >= limites.maxPorHora()) {
            throw new Errores.LimiteExcedido("Superaste el máximo de " + limites.maxPorHora() + " tickets por hora. Intenta más tarde.");
        }
        var ticket = tickets.save(Ticket.crear(datos.tipo(), datos.titulo(), datos.descripcion(), datos.sistemaReportado(),
                datos.canal(), usuario.id(), ahora));
        // [FLUJO CREAR_TICKET 3] Registra la creación en la bitácora de solo inserción → RegistroAuditoria.registrar
        auditar(usuario, "TICKET_CREADO", ticket, null);
        // [FLUJO CREAR_TICKET 4] Publica TicketCreado en memoria → ServicioSla.alCrearTicket (paso 5) arranca el reloj
        eventos.publishEvent(new EventosTicket.TicketCreado(ticket.id(), ticket.prioridad(), ahora, usuario.id()));
        // [FLUJO CREAR_TICKET 6] Deja TicketCreado enmascarado en el outbox, en esta misma transacción → TRIAJE_ASINCRONO 1
        publicador.registrar(ticket);
        return Dtos.TicketVista.de(ticket);
    }

    @Transactional(readOnly = true)
    Dtos.Pagina<Dtos.TicketVista> listar(UsuarioActual usuario, int pagina, int tamano) {
        var solicitud = PageRequest.of(Math.max(0, pagina), Math.clamp(tamano, 1, MAX_TAMANO_PAGINA), Sort.by("creadoEn").descending());
        var resultado = usuario.veTodosLosTickets()
                ? tickets.findAll(solicitud)
                : tickets.findBySolicitante(usuario.id(), solicitud);
        return new Dtos.Pagina<>(resultado.map(Dtos.TicketVista::de).getContent(), resultado.getNumber(), resultado.getSize(),
                resultado.getTotalElements());
    }

    @Transactional(readOnly = true)
    Dtos.TicketVista obtener(UsuarioActual usuario, UUID id) {
        return Dtos.TicketVista.de(cargarVisible(usuario, id));
    }

    // [FLUJO CAMBIAR_ESTADO 2] Carga el ticket visible, valida rol y transición; reabrir exige justificación → bitácora y evento
    @Transactional
    Dtos.TicketVista cambiarEstado(UsuarioActual usuario, UUID id, Dtos.CambiarEstado datos) {
        var ticket = cargarVisible(usuario, id);
        var anterior = ticket.estado();
        var destino = datos.estado();
        verificarPermisoDeEstado(usuario, ticket, destino);
        if (anterior.esReapertura(destino) && (datos.justificacion() == null || datos.justificacion().isBlank())) {
            throw new Errores.ReglaDeNegocio("JUSTIFICACION_REQUERIDA", "Reabrir un ticket exige una justificación.");
        }

        var antes = ticket.instantanea();
        var ahora = reloj.instant();
        ticket.cambiarEstado(destino, ahora);
        if (usuario.esPersonalDeSoporte()) {
            ticket.registrarRespuestaDeSoporte(ahora);
        }
        auditar(usuario, anterior.esReapertura(destino) ? "TICKET_REABIERTO" : "TICKET_ESTADO_CAMBIADO", ticket, antes,
                datos.justificacion());
        // [FLUJO CAMBIAR_ESTADO 3] Publica EstadoTicketCambiado → ServicioSla pausa, reanuda o detiene el reloj
        eventos.publishEvent(new EventosTicket.EstadoTicketCambiado(ticket.id(), anterior, destino, ahora, usuario.id()));
        return Dtos.TicketVista.de(ticket);
    }

    @Transactional
    Dtos.TicketVista cambiarPrioridad(UsuarioActual usuario, UUID id, Dtos.CambiarPrioridad datos) {
        var ticket = cargarVisible(usuario, id);
        exigirAlguno(usuario, Rol.TECNICO, Rol.COORDINADOR);
        var antes = ticket.instantanea();
        var anterior = ticket.prioridad();
        var ahora = reloj.instant();
        ticket.cambiarPrioridad(datos.prioridad(), ahora);
        ticket.registrarRespuestaDeSoporte(ahora);
        auditar(usuario, "TICKET_PRIORIDAD_CAMBIADA", ticket, antes, datos.justificacion());
        eventos.publishEvent(new EventosTicket.PrioridadTicketCambiada(ticket.id(), anterior, datos.prioridad(), ahora, usuario.id()));
        return Dtos.TicketVista.de(ticket);
    }

    /** El coordinador asigna a cualquiera; un técnico solo puede tomar el ticket para sí mismo. */
    @Transactional
    Dtos.TicketVista asignar(UsuarioActual usuario, UUID id, Dtos.Asignar datos) {
        var ticket = cargarVisible(usuario, id);
        var esCoordinador = usuario.tiene(Rol.COORDINADOR);
        var seLoTomaElTecnico = usuario.tiene(Rol.TECNICO) && usuario.id().equals(datos.tecnico());
        if (!esCoordinador && !seLoTomaElTecnico) {
            throw new Errores.AccionNoPermitida("Solo un coordinador asigna a otros; un técnico solo puede tomarlo para sí.");
        }

        var antes = ticket.instantanea();
        var anterior = ticket.estado();
        var ahora = reloj.instant();
        ticket.asignar(datos.tecnico(), ahora);
        ticket.registrarRespuestaDeSoporte(ahora);
        if (anterior == EstadoTicket.NUEVO || anterior == EstadoTicket.TRIADO) {
            ticket.cambiarEstado(EstadoTicket.ASIGNADO, ahora);
            eventos.publishEvent(new EventosTicket.EstadoTicketCambiado(ticket.id(), anterior, EstadoTicket.ASIGNADO, ahora, usuario.id()));
        }
        auditar(usuario, "TICKET_ASIGNADO", ticket, antes);
        return Dtos.TicketVista.de(ticket);
    }

    @Transactional
    Dtos.ComentarioVista comentar(UsuarioActual usuario, UUID id, Dtos.Comentar datos) {
        var ticket = cargarVisible(usuario, id);
        if (usuario.tiene(Rol.AUDITOR) && !usuario.esPersonalDeSoporte() && !ticket.solicitante().equals(usuario.id())) {
            throw new Errores.AccionNoPermitida("La auditoría es de solo lectura.");
        }
        ticket.verificarEditable();
        var ahora = reloj.instant();
        if (usuario.esPersonalDeSoporte()) {
            ticket.registrarRespuestaDeSoporte(ahora);
        }
        var comentario = comentarios.save(new Comentario(ticket.id(), usuario.id(), datos.texto(), ahora));
        auditoria.registrar(new EventoAuditoria(usuario.id(), "TICKET_COMENTADO", RECURSO, ticket.id().toString(), null,
                Map.of("comentarioId", comentario.id().toString())));
        return Dtos.ComentarioVista.de(comentario);
    }

    @Transactional(readOnly = true)
    List<Dtos.ComentarioVista> comentarios(UsuarioActual usuario, UUID id) {
        var ticket = cargarVisible(usuario, id);
        return comentarios.findByTicketIdOrderByCreadoEnAsc(ticket.id()).stream().map(Dtos.ComentarioVista::de).toList();
    }

    /**
     * Ticket ajeno e inexistente terminan igual: misma excepción, mismo mensaje y una sola consulta en los
     * dos casos, para no revelar qué existe ni por la respuesta ni por el tiempo (AGENTS.md).
     */
    private Ticket cargarVisible(UsuarioActual usuario, UUID id) {
        var encontrado = usuario.veTodosLosTickets() ? tickets.findById(id) : tickets.findByIdAndSolicitante(id, usuario.id());
        return encontrado.orElseThrow(() -> new Errores.NoEncontrado("El ticket no existe."));
    }

    /** Soporte mueve el ticket por cualquier transición válida; el solicitante solo confirma el cierre o lo reabre. */
    private static void verificarPermisoDeEstado(UsuarioActual usuario, Ticket ticket, EstadoTicket destino) {
        if (usuario.esPersonalDeSoporte()) {
            return;
        }
        var esDelSolicitante = ticket.solicitante().equals(usuario.id());
        var confirmaCierre = ticket.estado() == EstadoTicket.RESUELTO && destino == EstadoTicket.CERRADO;
        var reabre = ticket.estado().esReapertura(destino);
        if (!esDelSolicitante || !(confirmaCierre || reabre)) {
            throw new Errores.AccionNoPermitida("No puedes hacer ese cambio de estado.");
        }
    }

    private static void exigirAlguno(UsuarioActual usuario, Rol... roles) {
        for (var rol : roles) {
            if (usuario.tiene(rol)) {
                return;
            }
        }
        throw new Errores.AccionNoPermitida("Tu rol no permite esta acción.");
    }

    private void auditar(UsuarioActual usuario, String accion, Ticket ticket, Map<String, Object> antes) {
        auditar(usuario, accion, ticket, antes, null);
    }

    private void auditar(UsuarioActual usuario, String accion, Ticket ticket, Map<String, Object> antes, String justificacion) {
        var despues = ticket.instantanea();
        if (justificacion != null && !justificacion.isBlank()) {
            despues.put("justificacion", justificacion);
        }
        auditoria.registrar(new EventoAuditoria(usuario.id(), accion, RECURSO, ticket.id().toString(), antes, despues));
    }
}
