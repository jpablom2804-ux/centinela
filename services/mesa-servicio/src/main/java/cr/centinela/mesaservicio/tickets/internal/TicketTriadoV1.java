package cr.centinela.mesaservicio.tickets.internal;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * Contrato TicketTriado v1 (docs/EVENTOS.md). Todo lo que trae lo generó el modelo o lo puede falsificar quien
 * tenga acceso al broker: se valida completo al recibirlo, sin confiar en que el agente ya lo validó.
 */
record TicketTriadoV1(UUID id, String tipo, int version, String correlationId, String ocurridoEn, Datos datos) {

    static final String TIPO = "TicketTriado";
    static final int MAX_JUSTIFICACION = 500;
    static final int MAX_BORRADOR = 2000;
    static final int MAX_IDS = 5;

    private static final Set<String> ESTADOS = Set.of("completado", "invalido", "presupuesto_agotado", "error");
    private static final Set<String> PRIORIDADES = Set.of("P1", "P2", "P3", "P4");
    private static final Pattern CODIGO_CATALOGO = Pattern.compile("^[a-z_]{2,60}$");
    private static final Pattern CORRELACION = Pattern.compile("^[A-Za-z0-9-]{8,64}$");
    private static final Pattern CODIGO_ARTICULO = Pattern.compile("^KB-[0-9]{3,6}$");

    record Datos(UUID ticketId, String estadoAnalisis, String categoria, String sistemaAfectado, String prioridadSugerida,
            String justificacion, List<String> duplicadosPosibles, List<String> articulosSugeridos, String borradorRespuesta,
            Metricas metricas) {}

    record Metricas(String modelo, int tokensEntrada, int tokensSalida, int iteraciones, long duracionMs) {}

    boolean completado() {
        return "completado".equals(datos.estadoAnalisis());
    }

    /** Errores de forma; vacío si cumple el contrato. Que los IDs existan lo revisa el servicio. */
    List<String> errores() {
        var errores = new ArrayList<String>();
        if (id == null) errores.add("id vacío");
        if (!TIPO.equals(tipo)) errores.add("tipo no esperado");
        if (version != 1) errores.add("versión no soportada");
        if (correlationId == null || !CORRELACION.matcher(correlationId).matches()) errores.add("correlationId inválido");
        if (datos == null) {
            errores.add("sin datos");
            return errores;
        }
        if (datos.ticketId() == null) errores.add("ticketId vacío");
        if (!ESTADOS.contains(datos.estadoAnalisis())) errores.add("estadoAnalisis no válido");
        if (datos.duplicadosPosibles() == null || datos.duplicadosPosibles().size() > MAX_IDS) errores.add("duplicadosPosibles: hasta " + MAX_IDS);
        if (datos.articulosSugeridos() == null || datos.articulosSugeridos().size() > MAX_IDS) errores.add("articulosSugeridos: hasta " + MAX_IDS);
        validarMetricas(errores);
        if (!errores.isEmpty()) {
            return errores;
        }
        if (completado()) {
            validarSugerencia(errores);
        } else if (datos.categoria() != null || datos.sistemaAfectado() != null || datos.prioridadSugerida() != null
                || datos.justificacion() != null || datos.borradorRespuesta() != null
                || !datos.duplicadosPosibles().isEmpty() || !datos.articulosSugeridos().isEmpty()) {
            errores.add("un triaje sin completar no puede traer sugerencia");
        }
        return errores;
    }

    private void validarSugerencia(List<String> errores) {
        if (datos.categoria() == null || !CODIGO_CATALOGO.matcher(datos.categoria()).matches()) errores.add("categoria inválida");
        if (datos.sistemaAfectado() == null || !CODIGO_CATALOGO.matcher(datos.sistemaAfectado()).matches()) errores.add("sistemaAfectado inválido");
        if (!PRIORIDADES.contains(datos.prioridadSugerida())) errores.add("prioridadSugerida inválida");
        if (datos.justificacion() == null || datos.justificacion().isBlank() || datos.justificacion().length() > MAX_JUSTIFICACION) {
            errores.add("justificacion vacía o de más de " + MAX_JUSTIFICACION);
        }
        if (datos.borradorRespuesta() != null && datos.borradorRespuesta().length() > MAX_BORRADOR) errores.add("borradorRespuesta de más de " + MAX_BORRADOR);
        if (datos.duplicadosPosibles().stream().anyMatch(d -> !esUuid(d))) errores.add("duplicadosPosibles con IDs que no son de ticket");
        if (datos.articulosSugeridos().stream().anyMatch(a -> a == null || !CODIGO_ARTICULO.matcher(a).matches())) {
            errores.add("articulosSugeridos con códigos inválidos");
        }
    }

    private void validarMetricas(List<String> errores) {
        var m = datos.metricas();
        if (m == null || m.modelo() == null || m.modelo().isBlank() || m.modelo().length() > 100) {
            errores.add("metricas.modelo inválido");
        } else if (m.tokensEntrada() < 0 || m.tokensSalida() < 0 || m.iteraciones() < 0 || m.duracionMs() < 0) {
            errores.add("metricas negativas");
        }
    }

    private static boolean esUuid(String valor) {
        try {
            UUID.fromString(valor);
            return true;
        } catch (IllegalArgumentException | NullPointerException e) {
            return false;
        }
    }
}
