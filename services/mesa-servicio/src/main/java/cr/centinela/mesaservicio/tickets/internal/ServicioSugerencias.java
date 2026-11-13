package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.auditoria.EventoAuditoria;
import cr.centinela.mesaservicio.auditoria.RegistroAuditoria;
import cr.centinela.mesaservicio.conocimiento.api.IndiceSemantico;
import cr.centinela.mesaservicio.plataforma.Errores;
import cr.centinela.mesaservicio.plataforma.Rol;
import cr.centinela.mesaservicio.plataforma.UsuarioActual;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import java.time.Clock;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.UUID;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * Guarda la sugerencia del agente sin aplicarla (AGENTS.md: el agente recomienda, el técnico decide). Nada del
 * ticket cambia aquí: ni prioridad, ni estado, ni asignación.
 *
 * <p>A3, segunda mitad: aunque alguien publique un TicketTriado falso, solo se acepta si el ticket existe, sigue
 * en {@code nuevo} y no tiene sugerencia todavía. Los IDs sugeridos tienen que existir.
 */
@Service
class ServicioSugerencias {

    enum Resultado { GUARDADA, DESCARTADA_TICKET_INEXISTENTE, DESCARTADA_TICKET_YA_TRABAJADO, DUPLICADA }

    /** El mensaje no cumple el contrato: va a la DLQ. El texto describe el problema sin copiar el contenido. */
    static final class MensajeInvalido extends RuntimeException {
        MensajeInvalido(String motivo) {
            super(motivo);
        }
    }

    private final TicketRepositorio tickets;
    private final SugerenciaRepositorio sugerencias;
    private final IndiceSemantico indice;
    private final RegistroAuditoria auditoria;
    private final Clock reloj;

    ServicioSugerencias(TicketRepositorio tickets, SugerenciaRepositorio sugerencias, IndiceSemantico indice,
            RegistroAuditoria auditoria, Clock reloj) {
        this.tickets = tickets;
        this.sugerencias = sugerencias;
        this.indice = indice;
        this.auditoria = auditoria;
        this.reloj = reloj;
    }

    // [FLUJO TRIAJE_ASINCRONO 9] Valida contrato e IDs, descarta lo que no corresponde y guarda la sugerencia sin aplicarla + bitácora
    @Transactional
    Resultado registrar(TicketTriadoV1 triado) {
        var errores = triado.errores();
        if (!errores.isEmpty()) {
            throw new MensajeInvalido(String.join("; ", errores));
        }
        var ticketId = triado.datos().ticketId();
        var ticket = tickets.findById(ticketId);
        if (ticket.isEmpty()) {
            return Resultado.DESCARTADA_TICKET_INEXISTENTE;
        }
        if (sugerencias.existsById(ticketId)) {
            return Resultado.DUPLICADA;
        }
        if (ticket.get().estado() != EstadoTicket.NUEVO) {
            return Resultado.DESCARTADA_TICKET_YA_TRABAJADO;
        }
        verificarIds(triado, ticketId);

        sugerencias.saveAndFlush(new SugerenciaTriaje(triado, reloj.instant()));
        var despues = new LinkedHashMap<String, Object>();
        despues.put("estadoAnalisis", triado.datos().estadoAnalisis());
        despues.put("prioridadSugerida", triado.datos().prioridadSugerida());
        despues.put("modelo", triado.datos().metricas().modelo());
        despues.put("eventoId", triado.id().toString());
        auditoria.registrar(new EventoAuditoria("agente", "SUGERENCIA_TRIAJE_RECIBIDA", "ticket", ticketId.toString(), null, despues));
        return Resultado.GUARDADA;
    }

    /**
     * Solo soporte y auditoría ven la sugerencia; el rol se revisa antes que el ticket, así un solicitante recibe
     * la misma respuesta exista o no el ticket. Se muestra como recomendación: nada de esto está aplicado.
     */
    @Transactional(readOnly = true)
    Dtos.SugerenciaVista ver(UsuarioActual usuario, UUID ticketId) {
        if (!(usuario.tiene(Rol.TECNICO) || usuario.tiene(Rol.COORDINADOR) || usuario.tiene(Rol.AUDITOR))) {
            throw new Errores.AccionNoPermitida("Tu rol no permite ver la sugerencia del agente.");
        }
        if (!tickets.existsById(ticketId)) {
            throw new Errores.NoEncontrado("El ticket no existe.");
        }
        return sugerencias.findById(ticketId)
                .map(Dtos.SugerenciaVista::de)
                .orElseThrow(() -> new Errores.NoEncontrado("El ticket todavía no tiene sugerencia del agente."));
    }

    private void verificarIds(TicketTriadoV1 triado, UUID ticketId) {
        var duplicados = new HashSet<UUID>();
        triado.datos().duplicadosPosibles().forEach(d -> duplicados.add(UUID.fromString(d)));
        if (duplicados.contains(ticketId)) {
            throw new MensajeInvalido("un ticket no puede ser duplicado de sí mismo");
        }
        if (tickets.findAllById(duplicados).size() != duplicados.size()) {
            throw new MensajeInvalido("duplicadosPosibles con tickets que no existen");
        }
        var articulos = new HashSet<>(triado.datos().articulosSugeridos());
        if (!indice.articulosExistentes(articulos).containsAll(articulos)) {
            throw new MensajeInvalido("articulosSugeridos con artículos que no existen");
        }
    }
}
