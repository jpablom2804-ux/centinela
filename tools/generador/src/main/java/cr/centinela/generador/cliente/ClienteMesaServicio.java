package cr.centinela.generador.cliente;

import cr.centinela.generador.generacion.TicketSintetico;
import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Base64;

/**
 * Cliente de la API de mesa-servicio. El generador nunca toca la base: crea los tickets igual que una
 * persona, así pasan por las mismas validaciones, permisos, límites y bitácora (ADR-003).
 */
public final class ClienteMesaServicio {

    /** Resultado de un envío: el código HTTP y, si se creó, el id del ticket. */
    public record Resultado(int codigoHttp, String cuerpo) {
        public boolean creado() {
            return codigoHttp == 201;
        }
    }

    private final HttpClient http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(10)).build();
    private final URI tickets;

    public ClienteMesaServicio(URI base) {
        this.tickets = base.resolve("/api/tickets");
    }

    public Resultado crear(TicketSintetico ticket, String usuario, char[] clave) throws IOException, InterruptedException {
        var credenciales = Base64.getEncoder().encodeToString((usuario + ":" + new String(clave)).getBytes(StandardCharsets.UTF_8));
        var peticion = HttpRequest.newBuilder(tickets)
                .timeout(Duration.ofSeconds(30))
                .header("Authorization", "Basic " + credenciales)
                .header("Content-Type", "application/json; charset=utf-8")
                .POST(HttpRequest.BodyPublishers.ofString(aJson(ticket), StandardCharsets.UTF_8))
                .build();
        var respuesta = http.send(peticion, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        return new Resultado(respuesta.statusCode(), respuesta.body());
    }

    static String aJson(TicketSintetico t) {
        return "{" + campo("tipo", t.tipo()) + "," + campo("titulo", t.titulo()) + "," + campo("descripcion", t.descripcion())
                + "," + campo("sistemaReportado", t.sistemaReportado()) + "," + campo("canal", t.canal()) + "}";
    }

    private static String campo(String nombre, String valor) {
        return "\"" + nombre + "\":" + (valor == null ? "null" : "\"" + escapar(valor) + "\"");
    }

    /** Escapa lo que JSON exige (comillas, barra invertida y caracteres de control). */
    static String escapar(String texto) {
        var sb = new StringBuilder(texto.length() + 8);
        for (var c : texto.toCharArray()) {
            switch (c) {
                case '"' -> sb.append("\\\"");
                case '\\' -> sb.append("\\\\");
                case '\n' -> sb.append("\\n");
                case '\r' -> sb.append("\\r");
                case '\t' -> sb.append("\\t");
                default -> {
                    if (c < 0x20) {
                        sb.append(String.format("\\u%04x", (int) c));
                    } else {
                        sb.append(c);
                    }
                }
            }
        }
        return sb.toString();
    }
}
