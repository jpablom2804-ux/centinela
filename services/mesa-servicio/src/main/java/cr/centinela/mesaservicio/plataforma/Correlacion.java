package cr.centinela.mesaservicio.plataforma;

import java.util.UUID;
import java.util.regex.Pattern;
import org.slf4j.MDC;

/**
 * ID de correlación de punta a punta: viene de nginx en {@code X-Request-ID}, viaja en el sobre de cada
 * evento y lo repite el agente, así un ticket se sigue en los logs desde su creación hasta el triaje.
 */
public final class Correlacion {

    public static final String CABECERA = "X-Request-ID";
    public static final String CLAVE_MDC = "correlationId";
    // Lo que manda nginx ($request_id, 32 hexadecimales) o un UUID. Cualquier otra cosa se reemplaza:
    // es texto del cliente y termina en logs y mensajes.
    private static final Pattern VALIDO = Pattern.compile("^[A-Za-z0-9-]{8,64}$");

    private Correlacion() {}

    /** El ID de la petición en curso, o uno nuevo si el trabajo no viene de una petición. */
    public static String actual() {
        var valor = MDC.get(CLAVE_MDC);
        return valor != null ? valor : nuevo();
    }

    /** El valor si tiene un formato válido; si no (o si falta), uno nuevo. */
    public static String desdeCabecera(String valor) {
        return valor != null && VALIDO.matcher(valor).matches() ? valor : nuevo();
    }

    private static String nuevo() {
        return UUID.randomUUID().toString();
    }
}
