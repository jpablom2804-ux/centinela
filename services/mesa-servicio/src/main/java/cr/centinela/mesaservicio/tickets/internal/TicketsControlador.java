package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.plataforma.UsuarioActual;
import jakarta.validation.Valid;
import java.net.URI;
import java.util.List;
import java.util.UUID;
import org.springframework.http.ResponseEntity;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/** API de tickets. Sin lógica: traduce HTTP ↔ servicio. */
@RestController
@RequestMapping("/api/tickets")
class TicketsControlador {

    private final ServicioTickets servicio;
    private final ServicioSugerencias sugerencias;

    TicketsControlador(ServicioTickets servicio, ServicioSugerencias sugerencias) {
        this.servicio = servicio;
        this.sugerencias = sugerencias;
    }

    // [FLUJO CREAR_TICKET 1] Recibe el ticket del usuario autenticado y valida el cuerpo → ServicioTickets.crear (paso 2)
    @PostMapping
    ResponseEntity<Dtos.TicketVista> crear(Authentication auth, @Valid @RequestBody Dtos.CrearTicket datos) {
        var creado = servicio.crear(UsuarioActual.de(auth), datos);
        return ResponseEntity.created(URI.create("/api/tickets/" + creado.id())).body(creado);
    }

    @GetMapping
    Dtos.Pagina<Dtos.TicketVista> listar(Authentication auth,
            @RequestParam(defaultValue = "0") int pagina, @RequestParam(defaultValue = "20") int tamano) {
        return servicio.listar(UsuarioActual.de(auth), pagina, tamano);
    }

    @GetMapping("/{id}")
    Dtos.TicketVista obtener(Authentication auth, @PathVariable UUID id) {
        return servicio.obtener(UsuarioActual.de(auth), id);
    }

    // [FLUJO CAMBIAR_ESTADO 1] Recibe el estado destino y la justificación → ServicioTickets.cambiarEstado (paso 2)
    @PostMapping("/{id}/estado")
    Dtos.TicketVista cambiarEstado(Authentication auth, @PathVariable UUID id, @Valid @RequestBody Dtos.CambiarEstado datos) {
        return servicio.cambiarEstado(UsuarioActual.de(auth), id, datos);
    }

    @PostMapping("/{id}/prioridad")
    Dtos.TicketVista cambiarPrioridad(Authentication auth, @PathVariable UUID id, @Valid @RequestBody Dtos.CambiarPrioridad datos) {
        return servicio.cambiarPrioridad(UsuarioActual.de(auth), id, datos);
    }

    @PostMapping("/{id}/asignacion")
    Dtos.TicketVista asignar(Authentication auth, @PathVariable UUID id, @Valid @RequestBody Dtos.Asignar datos) {
        return servicio.asignar(UsuarioActual.de(auth), id, datos);
    }

    @PostMapping("/{id}/comentarios")
    ResponseEntity<Dtos.ComentarioVista> comentar(Authentication auth, @PathVariable UUID id, @Valid @RequestBody Dtos.Comentar datos) {
        var comentario = servicio.comentar(UsuarioActual.de(auth), id, datos);
        return ResponseEntity.status(201).body(comentario);
    }

    @GetMapping("/{id}/comentarios")
    List<Dtos.ComentarioVista> comentarios(Authentication auth, @PathVariable UUID id) {
        return servicio.comentarios(UsuarioActual.de(auth), id);
    }

    /** La sugerencia del agente: solo soporte y auditoría, y nunca aplicada (la decide un técnico). */
    @GetMapping("/{id}/sugerencia")
    Dtos.SugerenciaVista sugerencia(Authentication auth, @PathVariable UUID id) {
        return sugerencias.ver(UsuarioActual.de(auth), id);
    }
}
