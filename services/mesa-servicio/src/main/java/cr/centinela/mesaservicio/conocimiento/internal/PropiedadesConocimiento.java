package cr.centinela.mesaservicio.conocimiento.internal;

import java.net.URI;
import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * Todo por configuración (AGENTS.md): proveedor, modelo, umbrales y límites. La API key llega como secreto en
 * archivo (voyage_api_key, ADR-005); vacía = sin embeddings, se busca solo por texto.
 *
 * @param url                 endpoint de embeddings de Voyage AI
 * @param modelo              modelo de embeddings; cambiarlo hace que todo se vuelva a indexar
 * @param dimension           dimensión del vector; fija en la migración (vector(1024))
 * @param apiKey              secreto voyage_api_key
 * @param umbralSimilitud     similitud coseno mínima para considerar algo parecido
 * @param indexacion          cada cuánto se calculan los vectores pendientes
 * @param lote                textos por llamada al proveedor
 * @param maxIntentos         después de esto un texto queda sin vector (se sigue encontrando por texto)
 * @param maxEspera           tope del backoff entre intentos
 * @param pausaTrasErrorPermanente si la key es inválida o no hay saldo, no se insiste durante este tiempo
 * @param timeout             tiempo máximo de cada llamada al proveedor
 */
@ConfigurationProperties("centinela.conocimiento")
record PropiedadesConocimiento(URI url, String modelo, int dimension, String apiKey, double umbralSimilitud,
        Duration indexacion, int lote, int maxIntentos, Duration maxEspera, Duration pausaTrasErrorPermanente,
        Duration timeout) {

    boolean conEmbeddings() {
        return apiKey != null && !apiKey.isBlank();
    }
}
