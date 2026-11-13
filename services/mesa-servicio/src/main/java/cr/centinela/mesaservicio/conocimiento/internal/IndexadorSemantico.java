package cr.centinela.mesaservicio.conocimiento.internal;

import java.time.Clock;
import java.time.Duration;
import java.time.ZoneOffset;
import java.util.List;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.annotation.Profile;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Calcula en lotes los vectores pendientes, fuera de la transacción del usuario: crear un ticket nunca espera a
 * Voyage (ADR-007). Varias réplicas se reparten el trabajo con SKIP LOCKED. Un texto cuyo modelo no es el
 * configurado también cuenta como pendiente: cambiar de modelo vuelve a indexar todo, sin mezclar vectores.
 */
@Component
@Profile("!migracion")
public class IndexadorSemantico {

    private static final Logger log = LoggerFactory.getLogger(IndexadorSemantico.class);

    private static final String TOMAR_PENDIENTES = """
            SELECT recurso_tipo, recurso_id, texto, intentos
              FROM mesa_servicio.indice_semantico
             WHERE (embedding IS NULL OR modelo IS DISTINCT FROM ?) AND intentos < ? AND proximo_intento <= ?
             ORDER BY actualizado_en
             LIMIT ?
               FOR UPDATE SKIP LOCKED
            """;

    private final JdbcTemplate jdbc;
    private final ProveedorEmbeddings proveedor;
    private final PropiedadesConocimiento propiedades;
    private final TransactionTemplate transaccion;
    private final Clock reloj;

    IndexadorSemantico(JdbcTemplate jdbc, ProveedorEmbeddings proveedor, PropiedadesConocimiento propiedades,
            TransactionTemplate transaccion, Clock reloj) {
        this.jdbc = jdbc;
        this.proveedor = proveedor;
        this.propiedades = propiedades;
        this.transaccion = transaccion;
        this.reloj = reloj;
    }

    @Scheduled(fixedDelayString = "${centinela.conocimiento.indexacion}", initialDelayString = "${centinela.conocimiento.indexacion}")
    void programado() {
        indexarPendientes();
    }

    /** @return cuántos textos quedaron con vector en esta vuelta */
    public int indexarPendientes() {
        if (!proveedor.habilitado()) {
            return 0;
        }
        Integer indexados = transaccion.execute(estado -> {
            List<Pendiente> pendientes = jdbc.query(TOMAR_PENDIENTES,
                    (fila, i) -> new Pendiente(fila.getString(1), fila.getString(2), fila.getString(3), fila.getInt(4)),
                    proveedor.modelo(), propiedades.maxIntentos(), reloj.instant().atOffset(ZoneOffset.UTC), propiedades.lote());
            if (pendientes.isEmpty()) {
                return 0;
            }
            try {
                var vectores = proveedor.vectorizar(pendientes.stream().map(Pendiente::texto).toList(), ProveedorEmbeddings.Uso.DOCUMENTO);
                var ahora = reloj.instant().atOffset(ZoneOffset.UTC);
                for (var i = 0; i < pendientes.size(); i++) {
                    var p = pendientes.get(i);
                    jdbc.update("""
                            UPDATE mesa_servicio.indice_semantico
                               SET embedding = ?::vector, modelo = ?, intentos = 0, ultimo_error = NULL, actualizado_en = ?
                             WHERE recurso_tipo = ? AND recurso_id = ?
                            """, IndiceSemanticoPostgres.literalVector(vectores.get(i)), proveedor.modelo(), ahora, p.tipo(), p.id());
                }
                return pendientes.size();
            } catch (ProveedorEmbeddings.ErrorEmbeddings e) {
                for (var p : pendientes) {
                    var espera = esperaTras(p.intentos() + 1);
                    jdbc.update("""
                            UPDATE mesa_servicio.indice_semantico
                               SET intentos = intentos + 1, ultimo_error = ?, proximo_intento = ?
                             WHERE recurso_tipo = ? AND recurso_id = ?
                            """, e.codigo(), reloj.instant().plus(espera).atOffset(ZoneOffset.UTC), p.tipo(), p.id());
                }
                log.warn("No se pudieron calcular {} embeddings ({}); se reintenta con espera", pendientes.size(), e.codigo());
                return 0;
            }
        });
        return indexados == null ? 0 : indexados;
    }

    private Duration esperaTras(int intentos) {
        var espera = Duration.ofSeconds(1L << Math.min(intentos, 20));
        return espera.compareTo(propiedades.maxEspera()) > 0 ? propiedades.maxEspera() : espera;
    }

    private record Pendiente(String tipo, String id, String texto, int intentos) {}
}
