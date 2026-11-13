package cr.centinela.mesaservicio.conocimiento.internal;

import cr.centinela.mesaservicio.plataforma.Enmascarador;
import java.io.IOException;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.http.HttpTimeoutException;
import java.time.Clock;
import java.time.Instant;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.concurrent.atomic.AtomicReference;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;
import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;

/**
 * Cliente HTTP escrito a mano para {@code POST /v1/embeddings} de Voyage AI. Verifica otra vez el enmascarado
 * antes de enviar: si algo se detecta, no llama. Nunca registra textos ni la key; solo códigos y cantidades.
 */
@Component
class ClienteEmbeddingsVoyage implements ProveedorEmbeddings {

    private static final Logger log = LoggerFactory.getLogger(ClienteEmbeddingsVoyage.class);

    private final PropiedadesConocimiento propiedades;
    private final ObjectMapper json;
    private final Clock reloj;
    private final HttpClient http;
    /** Tras un error permanente (key inválida, sin saldo) no se insiste hasta este instante. */
    private final AtomicReference<Instant> pausadoHasta = new AtomicReference<>(Instant.MIN);

    ClienteEmbeddingsVoyage(PropiedadesConocimiento propiedades, ObjectMapper json, Clock reloj) {
        this.propiedades = propiedades;
        this.json = json;
        this.reloj = reloj;
        // HTTP/1.1: con HTTP/2 la conexión con Voyage se quedaba colgada hasta el timeout.
        this.http = HttpClient.newBuilder().version(HttpClient.Version.HTTP_1_1).connectTimeout(propiedades.timeout()).build();
    }

    @Override
    public boolean habilitado() {
        return propiedades.conEmbeddings() && reloj.instant().isAfter(pausadoHasta.get());
    }

    @Override
    public String modelo() {
        return propiedades.modelo();
    }

    @Override
    public List<float[]> vectorizar(List<String> textos, Uso uso) {
        if (!habilitado()) {
            throw new ErrorEmbeddings("DESHABILITADO", false);
        }
        // Segunda barrera: nada sin enmascarar sale hacia el proveedor (AGENTS.md).
        if (textos.stream().anyMatch(t -> !Enmascarador.detectar(t).isEmpty())) {
            throw new ErrorEmbeddings("TEXTO_SIN_ENMASCARAR", false);
        }

        var cuerpo = new LinkedHashMap<String, Object>();
        cuerpo.put("input", textos);
        cuerpo.put("model", propiedades.modelo());
        cuerpo.put("input_type", uso == Uso.CONSULTA ? "query" : "document");
        cuerpo.put("output_dimension", propiedades.dimension());
        var peticion = HttpRequest.newBuilder(propiedades.url())
                .timeout(propiedades.timeout())
                .header("Authorization", "Bearer " + propiedades.apiKey())
                .header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(json.writeValueAsString(cuerpo)))
                .build();

        HttpResponse<String> respuesta;
        try {
            respuesta = http.send(peticion, HttpResponse.BodyHandlers.ofString());
        } catch (HttpTimeoutException e) {
            throw new ErrorEmbeddings("TIMEOUT", true);
        } catch (IOException e) {
            throw new ErrorEmbeddings("SIN_CONEXION", true);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new ErrorEmbeddings("INTERRUMPIDO", true);
        }

        var estado = respuesta.statusCode();
        if (estado != 200) {
            throw clasificar(estado);
        }
        return leerVectores(json.readTree(respuesta.body()), textos.size());
    }

    private ErrorEmbeddings clasificar(int estado) {
        if (estado == 429 || estado >= 500) {
            return new ErrorEmbeddings("HTTP_" + estado, true);
        }
        // 400, 401, 402, 403…: reintentar no sirve. Se pausa y se alerta; mientras, se busca por texto.
        var hasta = reloj.instant().plus(propiedades.pausaTrasErrorPermanente());
        pausadoHasta.set(hasta);
        log.error("Voyage AI respondió {}: embeddings pausados hasta {}; mientras, se busca por texto", estado, hasta);
        return new ErrorEmbeddings("HTTP_" + estado, false);
    }

    private List<float[]> leerVectores(JsonNode raiz, int esperados) {
        var datos = raiz.path("data");
        if (!datos.isArray() || datos.size() != esperados) {
            throw new ErrorEmbeddings("RESPUESTA_INVALIDA", false);
        }
        var vectores = new ArrayList<float[]>(esperados);
        for (var i = 0; i < esperados; i++) {
            vectores.add(null);
        }
        for (var elemento : datos) {
            var indice = elemento.path("index").asInt(-1);
            var embedding = elemento.path("embedding");
            if (indice < 0 || indice >= esperados || embedding.size() != propiedades.dimension()) {
                throw new ErrorEmbeddings("RESPUESTA_INVALIDA", false);
            }
            var vector = new float[embedding.size()];
            for (var j = 0; j < vector.length; j++) {
                vector[j] = (float) embedding.get(j).asDouble();
            }
            vectores.set(indice, vector);
        }
        log.debug("Embeddings: {} textos, {} tokens", esperados, raiz.path("usage").path("total_tokens").asInt());
        return vectores;
    }
}
