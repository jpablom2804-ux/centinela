package cr.centinela.mesaservicio.auditoria.internal;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.Objects;

/**
 * Encadena la bitácora (A8): cada registro guarda el hash del anterior. Insertar un registro inventado
 * en medio, borrarlo o cambiarlo rompe la cadena desde ese punto, y la verificación lo detecta.
 */
final class CadenaHash {

    static final String INICIO = "0".repeat(64);

    private CadenaHash() {}

    static String calcular(EntradaBitacora e) {
        var contenido = String.join("\u001F",
                Objects.toString(e.hashAnterior(), INICIO),
                e.ocurridoEn().toString(),
                e.actor(),
                e.accion(),
                e.recursoTipo(),
                e.recursoId(),
                Objects.toString(e.antes(), ""),
                Objects.toString(e.despues(), ""));
        try {
            var digest = MessageDigest.getInstance("SHA-256").digest(contenido.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e1) {
            throw new IllegalStateException("SHA-256 no está disponible en esta JVM", e1);
        }
    }
}
