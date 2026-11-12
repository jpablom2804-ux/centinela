package cr.centinela.mesaservicio.tickets.internal;

import cr.centinela.mesaservicio.plataforma.Errores;
import cr.centinela.mesaservicio.tickets.api.Canal;
import cr.centinela.mesaservicio.tickets.api.EstadoTicket;
import cr.centinela.mesaservicio.tickets.api.Prioridad;
import cr.centinela.mesaservicio.tickets.api.TipoTicket;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import java.time.Instant;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/** Ticket con sus reglas: transiciones válidas y "un ticket cerrado no se edita". */
@Entity
@Table(schema = "mesa_servicio", name = "ticket")
class Ticket {

    /** Prioridad mientras nadie la revisa; el técnico la ajusta al triar. La del agente nunca se aplica sola. */
    static final Prioridad PRIORIDAD_INICIAL = Prioridad.P3;

    @Id
    private UUID id;

    @Enumerated(EnumType.STRING)
    private TipoTicket tipo;

    private String titulo;
    private String descripcion;
    private String sistemaReportado;

    @Enumerated(EnumType.STRING)
    private Canal canal;

    @Enumerated(EnumType.STRING)
    private EstadoTicket estado;

    @Enumerated(EnumType.STRING)
    private Prioridad prioridad;

    private String solicitante;

    // Con la "A" final la conversión automática daría "asignadoa": el nombre va explícito.
    @Column(name = "asignado_a")
    private String asignadoA;
    private Instant primeraRespuestaEn;
    private Instant creadoEn;
    private Instant actualizadoEn;

    @Version
    private Long version;

    protected Ticket() {}

    static Ticket crear(TipoTicket tipo, String titulo, String descripcion, String sistemaReportado, Canal canal,
            String solicitante, Instant ahora) {
        var t = new Ticket();
        t.id = UUID.randomUUID();
        t.tipo = tipo;
        t.titulo = titulo;
        t.descripcion = descripcion;
        t.sistemaReportado = sistemaReportado;
        t.canal = canal;
        t.estado = EstadoTicket.NUEVO;
        t.prioridad = PRIORIDAD_INICIAL;
        t.solicitante = solicitante;
        t.creadoEn = ahora;
        t.actualizadoEn = ahora;
        return t;
    }

    void cambiarEstado(EstadoTicket destino, Instant ahora) {
        if (!estado.puedePasarA(destino)) {
            throw new Errores.ReglaDeNegocio("TRANSICION_INVALIDA",
                    "No se puede pasar de " + estado.codigo() + " a " + destino.codigo() + ".");
        }
        estado = destino;
        actualizadoEn = ahora;
    }

    void cambiarPrioridad(Prioridad nueva, Instant ahora) {
        verificarEditable();
        prioridad = nueva;
        actualizadoEn = ahora;
    }

    void asignar(String tecnico, Instant ahora) {
        verificarEditable();
        asignadoA = tecnico;
        actualizadoEn = ahora;
    }

    /** Guarda la primera vez que soporte actúa sobre el ticket (base del KPI de tiempo de respuesta). */
    void registrarRespuestaDeSoporte(Instant ahora) {
        if (primeraRespuestaEn == null) {
            primeraRespuestaEn = ahora;
        }
    }

    void verificarEditable() {
        if (estado.esFinal()) {
            throw new Errores.ReglaDeNegocio("TICKET_CERRADO", "Un ticket cerrado no se edita: hay que reabrirlo con justificación.");
        }
    }

    /** Lo que cambia y se registra en la bitácora (antes y después). */
    Map<String, Object> instantanea() {
        var datos = new HashMap<String, Object>();
        datos.put("estado", estado.codigo());
        datos.put("prioridad", prioridad.name());
        datos.put("asignadoA", asignadoA);
        return datos;
    }

    UUID id() { return id; }
    TipoTicket tipo() { return tipo; }
    String titulo() { return titulo; }
    String descripcion() { return descripcion; }
    String sistemaReportado() { return sistemaReportado; }
    Canal canal() { return canal; }
    EstadoTicket estado() { return estado; }
    Prioridad prioridad() { return prioridad; }
    String solicitante() { return solicitante; }
    String asignadoA() { return asignadoA; }
    Instant creadoEn() { return creadoEn; }
    Instant actualizadoEn() { return actualizadoEn; }
}
