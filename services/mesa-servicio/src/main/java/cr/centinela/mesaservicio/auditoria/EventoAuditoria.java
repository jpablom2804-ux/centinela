package cr.centinela.mesaservicio.auditoria;

import java.util.Map;

/**
 * Una acción que queda en la bitácora: quién, qué, sobre qué recurso, y el antes y el después.
 * {@code antes} y {@code despues} pueden ser null (creaciones y lecturas no tienen "antes").
 */
public record EventoAuditoria(
        String actor, String accion, String recursoTipo, String recursoId, Map<String, ?> antes, Map<String, ?> despues) {}
