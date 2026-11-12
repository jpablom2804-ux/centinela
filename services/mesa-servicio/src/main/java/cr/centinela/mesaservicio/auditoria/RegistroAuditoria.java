package cr.centinela.mesaservicio.auditoria;

import cr.centinela.mesaservicio.auditoria.internal.Bitacora;
import java.util.Map;
import java.util.TreeMap;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import tools.jackson.databind.ObjectMapper;

/**
 * API del módulo: registra acciones en la bitácora de solo inserción. Se ejecuta dentro de la
 * transacción de quien llama, así la acción y su registro se guardan juntos o no se guarda ninguno.
 */
@Service
public class RegistroAuditoria {

    private final Bitacora bitacora;
    private final ObjectMapper json;

    RegistroAuditoria(Bitacora bitacora, ObjectMapper json) {
        this.bitacora = bitacora;
        this.json = json;
    }

    @Transactional(propagation = Propagation.MANDATORY)
    public void registrar(EventoAuditoria evento) {
        bitacora.agregar(
                evento.actor(),
                evento.accion(),
                evento.recursoTipo(),
                evento.recursoId(),
                aJson(evento.antes()),
                aJson(evento.despues()));
    }

    /** Recalcula toda la cadena de hashes. Devuelve el id del primer registro alterado, o null si está íntegra. */
    @Transactional(readOnly = true)
    public Long primerRegistroAlterado() {
        return bitacora.primerRegistroAlterado();
    }

    // Claves ordenadas: el mismo contenido produce siempre el mismo JSON.
    private String aJson(Map<String, ?> datos) {
        return datos == null ? null : json.writeValueAsString(new TreeMap<>(datos));
    }
}
