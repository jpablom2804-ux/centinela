package cr.centinela.generador.cli;

import cr.centinela.generador.cliente.ClienteMesaServicio;
import cr.centinela.generador.generacion.GeneradorTickets;
import java.io.IOException;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;

/**
 * Crea tickets sintéticos por la API. Uso:
 *
 * <pre>
 * java -jar generador.jar [--cantidad 20] [--url http://127.0.0.1:8080] [--semilla 42] [--usuarios solicitante1,solicitante2]
 * </pre>
 *
 * La contraseña de los usuarios de prueba se lee de CENTINELA_SECRETOS/usuarios_prueba_password: nunca se pasa
 * como argumento, para que no quede en el historial de la terminal ni en la lista de procesos.
 */
public final class Generador {

    private static final int MAX_CANTIDAD = 500;

    private Generador() {}

    public static void main(String[] args) throws IOException, InterruptedException {
        var opciones = leerOpciones(args);
        var cantidad = Integer.parseInt(opciones.getOrDefault("cantidad", "20"));
        if (cantidad < 1 || cantidad > MAX_CANTIDAD) {
            System.err.println("--cantidad debe estar entre 1 y " + MAX_CANTIDAD);
            System.exit(64);
        }
        var url = URI.create(opciones.getOrDefault("url", "http://127.0.0.1:8080"));
        var semilla = Long.parseLong(opciones.getOrDefault("semilla", String.valueOf(System.nanoTime())));
        var usuarios = List.of(opciones.getOrDefault("usuarios", "solicitante1,solicitante2").split(","));
        var clave = leerClave();

        var generador = new GeneradorTickets(semilla);
        var cliente = new ClienteMesaServicio(url);
        var limitados = new HashSet<String>();
        int creados = 0;
        int rechazados = 0;

        try {
            for (var i = 0; i < cantidad && limitados.size() < usuarios.size(); i++) {
                var usuario = usuarios.get(i % usuarios.size());
                if (limitados.contains(usuario)) {
                    continue;
                }
                var resultado = cliente.crear(generador.siguiente(), usuario, clave);
                if (resultado.creado()) {
                    creados++;
                } else if (resultado.codigoHttp() == 429) {
                    // El límite por usuario (A5) se respeta: ese usuario no envía más en esta corrida.
                    System.err.println(usuario + " llegó a su límite de tickets por hora.");
                    limitados.add(usuario);
                } else if (resultado.codigoHttp() == 401 || resultado.codigoHttp() == 403) {
                    System.err.println("Credenciales rechazadas para " + usuario + " (HTTP " + resultado.codigoHttp() + "). ¿Corre mesa-servicio con el perfil desarrollo?");
                    System.exit(1);
                } else {
                    rechazados++;
                    System.err.println("HTTP " + resultado.codigoHttp() + ": " + resultado.cuerpo());
                }
            }
        } finally {
            Arrays.fill(clave, '\0');
        }

        System.out.printf("Semilla %d: %d tickets creados, %d rechazados, %d usuarios en su límite.%n", semilla, creados, rechazados,
                limitados.size());
    }

    private static char[] leerClave() throws IOException {
        var carpeta = System.getenv("CENTINELA_SECRETOS");
        if (carpeta == null || carpeta.isBlank()) {
            System.err.println("Falta la variable CENTINELA_SECRETOS con la ruta absoluta de la carpeta secrets/.");
            System.exit(78);
        }
        var archivo = Path.of(carpeta, "usuarios_prueba_password");
        return new String(Files.readAllBytes(archivo), StandardCharsets.UTF_8).strip().toCharArray();
    }

    private static Map<String, String> leerOpciones(String[] args) {
        var opciones = new HashMap<String, String>();
        for (var i = 0; i + 1 < args.length; i += 2) {
            if (!args[i].startsWith("--")) {
                System.err.println("Opción no reconocida: " + args[i]);
                System.exit(64);
            }
            opciones.put(args[i].substring(2), args[i + 1]);
        }
        return opciones;
    }
}
