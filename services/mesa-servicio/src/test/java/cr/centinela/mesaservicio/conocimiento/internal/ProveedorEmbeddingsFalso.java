package cr.centinela.mesaservicio.conocimiento.internal;

import java.text.Normalizer;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Embeddings deterministas sin red: cada palabra suma en una posición del vector (bolsa de palabras normalizada).
 * Textos con palabras en común quedan cerca, que alcanza para probar el camino por significado. Apagado por
 * defecto: así las demás pruebas usan el respaldo por texto, como un clon del repositorio sin key de Voyage.
 */
public class ProveedorEmbeddingsFalso implements ProveedorEmbeddings {

    private final AtomicBoolean habilitado = new AtomicBoolean(false);
    private final List<String> recibidos = new CopyOnWriteArrayList<>();

    public void habilitar(boolean valor) {
        habilitado.set(valor);
    }

    /** Todo lo que "salió" hacia el proveedor: para verificar que iba enmascarado. */
    public List<String> recibidos() {
        return recibidos;
    }

    @Override
    public boolean habilitado() {
        return habilitado.get();
    }

    @Override
    public String modelo() {
        return "modelo-falso";
    }

    @Override
    public List<float[]> vectorizar(List<String> textos, Uso uso) {
        if (!habilitado()) {
            throw new ErrorEmbeddings("DESHABILITADO", false);
        }
        recibidos.addAll(textos);
        var vectores = new ArrayList<float[]>();
        for (var texto : textos) {
            var vector = new float[1024];
            var limpio = Normalizer.normalize(texto.toLowerCase(Locale.ROOT), Normalizer.Form.NFD).replaceAll("\\p{M}", "");
            for (var palabra : limpio.split("[^a-z0-9]+")) {
                if (palabra.length() >= 3) {
                    vector[Math.floorMod(palabra.hashCode(), vector.length)] += 1f;
                }
            }
            var norma = 0.0;
            for (var v : vector) norma += v * v;
            norma = Math.sqrt(norma);
            if (norma > 0) {
                for (var i = 0; i < vector.length; i++) vector[i] /= (float) norma;
            } else {
                vector[0] = 1f;
            }
            vectores.add(vector);
        }
        return vectores;
    }
}
