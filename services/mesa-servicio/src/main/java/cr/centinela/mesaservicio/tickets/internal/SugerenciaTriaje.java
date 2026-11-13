package cr.centinela.mesaservicio.tickets.internal;

import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.Arrays;
import java.util.List;
import java.util.UUID;
import org.hibernate.annotations.Immutable;
import org.springframework.data.domain.Persistable;

/**
 * Lo que recomendó el agente para un ticket. Inmutable: es un registro de lo que llegó, no un estado del ticket.
 * Los textos los generó el modelo: son no confiables y se muestran como texto plano.
 */
@Entity
@Immutable
@Table(schema = "mesa_servicio", name = "sugerencia_triaje")
class SugerenciaTriaje implements Persistable<UUID> {

    @Id
    private UUID ticketId;
    private UUID eventoId;
    private String correlationId;
    private String estadoAnalisis;
    private String categoria;
    private String sistemaAfectado;
    private String prioridadSugerida;
    private String justificacion;
    private String duplicadosPosibles;
    private String articulosSugeridos;
    private String borradorRespuesta;
    private String modelo;
    private int tokensEntrada;
    private int tokensSalida;
    private int iteraciones;
    private long duracionMs;
    private Instant recibidaEn;

    protected SugerenciaTriaje() {}

    SugerenciaTriaje(TicketTriadoV1 triado, Instant ahora) {
        var d = triado.datos();
        this.ticketId = d.ticketId();
        this.eventoId = triado.id();
        this.correlationId = triado.correlationId();
        this.estadoAnalisis = d.estadoAnalisis();
        this.categoria = d.categoria();
        this.sistemaAfectado = d.sistemaAfectado();
        this.prioridadSugerida = d.prioridadSugerida();
        this.justificacion = d.justificacion();
        this.duplicadosPosibles = String.join(",", d.duplicadosPosibles());
        this.articulosSugeridos = String.join(",", d.articulosSugeridos());
        this.borradorRespuesta = d.borradorRespuesta();
        this.modelo = d.metricas().modelo();
        this.tokensEntrada = d.metricas().tokensEntrada();
        this.tokensSalida = d.metricas().tokensSalida();
        this.iteraciones = d.metricas().iteraciones();
        this.duracionMs = d.metricas().duracionMs();
        this.recibidaEn = ahora;
    }

    UUID ticketId() { return ticketId; }

    // Solo se inserta (es inmutable): así Spring Data hace persist y no merge, y una carrera entre réplicas
    // termina en una violación de clave primaria en vez de una actualización.
    @Override
    public UUID getId() { return ticketId; }

    @Override
    public boolean isNew() { return true; }

    UUID eventoId() { return eventoId; }
    String estadoAnalisis() { return estadoAnalisis; }
    String categoria() { return categoria; }
    String sistemaAfectado() { return sistemaAfectado; }
    String prioridadSugerida() { return prioridadSugerida; }
    String justificacion() { return justificacion; }
    List<String> duplicadosPosibles() { return separar(duplicadosPosibles); }
    List<String> articulosSugeridos() { return separar(articulosSugeridos); }
    String borradorRespuesta() { return borradorRespuesta; }
    String modelo() { return modelo; }
    int tokensEntrada() { return tokensEntrada; }
    int tokensSalida() { return tokensSalida; }
    Instant recibidaEn() { return recibidaEn; }

    private static List<String> separar(String valor) {
        return valor == null || valor.isEmpty() ? List.of() : Arrays.asList(valor.split(","));
    }
}
