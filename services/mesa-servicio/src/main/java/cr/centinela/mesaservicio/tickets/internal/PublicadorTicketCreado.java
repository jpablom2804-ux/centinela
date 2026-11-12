package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.plataforma.Enmascarador;
import cr.centinela.mesaservicio.plataforma.mensajeria.MensajeSaliente;
import cr.centinela.mesaservicio.plataforma.mensajeria.Outbox;
import java.util.LinkedHashMap;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;

/** Arma el contrato TicketCreado v1 (docs/EVENTOS.md) con el texto libre ya enmascarado. */
@Component
class PublicadorTicketCreado {

    static final String EXCHANGE = "tickets";
    static final String RUTA = "ticket.creado";
    private static final String TIPO = "TicketCreado";
    private static final int VERSION = 1;
    private static final Logger log = LoggerFactory.getLogger(PublicadorTicketCreado.class);

    private final Outbox outbox;

    PublicadorTicketCreado(Outbox outbox) {
        this.outbox = outbox;
    }

    // [FLUJO TRIAJE_ASINCRONO 1] Enmascara título, descripción y sistema reportado; el ticket original no cambia → Outbox.registrar (paso 2)
    void registrar(Ticket ticket) {
        var datos = new LinkedHashMap<String, Object>();
        datos.put("ticketId", ticket.id().toString());
        datos.put("tipo", ticket.tipo().codigo());
        datos.put("titulo", enmascarar(ticket, ticket.titulo()));
        datos.put("descripcion", enmascarar(ticket, ticket.descripcion()));
        datos.put("sistemaReportado", enmascarar(ticket, ticket.sistemaReportado()));
        datos.put("canal", ticket.canal().codigo());
        outbox.registrar(new MensajeSaliente(EXCHANGE, RUTA, TIPO, VERSION, ticket.creadoEn(), datos));
    }

    private static String enmascarar(Ticket ticket, String texto) {
        var resultado = Enmascarador.enmascarar(texto);
        if (Enmascarador.TEXTO_RETENIDO.equals(resultado)) {
            log.warn("Ticket {}: un texto se retuvo completo porque no se pudo enmascarar con seguridad", ticket.id());
        }
        return resultado;
    }
}
