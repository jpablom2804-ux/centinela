package cr.centinela.mesaservicio.plataforma;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.orm.ObjectOptimisticLockingFailureException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.method.annotation.MethodArgumentTypeMismatchException;

/** Errores de la API siempre como { codigo, mensaje }; nunca stack traces ni detalles internos. */
@RestControllerAdvice
class ManejadorErrores {

    private static final Logger log = LoggerFactory.getLogger(ManejadorErrores.class);

    record ErrorApi(String codigo, String mensaje) {}

    @ExceptionHandler(Errores.NoEncontrado.class)
    ResponseEntity<ErrorApi> noEncontrado(Errores.NoEncontrado e) {
        return respuesta(HttpStatus.NOT_FOUND, "NO_ENCONTRADO", e.getMessage());
    }

    @ExceptionHandler(Errores.AccionNoPermitida.class)
    ResponseEntity<ErrorApi> noPermitida(Errores.AccionNoPermitida e) {
        return respuesta(HttpStatus.FORBIDDEN, "ACCION_NO_PERMITIDA", e.getMessage());
    }

    @ExceptionHandler(Errores.ReglaDeNegocio.class)
    ResponseEntity<ErrorApi> regla(Errores.ReglaDeNegocio e) {
        return respuesta(HttpStatus.CONFLICT, e.codigo(), e.getMessage());
    }

    @ExceptionHandler(MethodArgumentNotValidException.class)
    ResponseEntity<ErrorApi> invalido(MethodArgumentNotValidException e) {
        var campo = e.getBindingResult().getFieldErrors().stream()
                .map(f -> f.getField() + ": " + f.getDefaultMessage())
                .findFirst()
                .orElse("datos inválidos");
        return respuesta(HttpStatus.BAD_REQUEST, "DATOS_INVALIDOS", campo);
    }

    /** Cuerpo ilegible o con campos que esa acción no acepta (A6: asignación masiva). */
    @ExceptionHandler({HttpMessageNotReadableException.class, MethodArgumentTypeMismatchException.class})
    ResponseEntity<ErrorApi> ilegible(Exception e) {
        return respuesta(HttpStatus.BAD_REQUEST, "DATOS_INVALIDOS", "El cuerpo tiene campos no permitidos o un formato inválido.");
    }

    @ExceptionHandler(ObjectOptimisticLockingFailureException.class)
    ResponseEntity<ErrorApi> concurrencia(ObjectOptimisticLockingFailureException e) {
        return respuesta(HttpStatus.CONFLICT, "MODIFICADO_POR_OTRO", "Alguien más modificó el recurso. Vuelve a cargarlo.");
    }

    @ExceptionHandler(Exception.class)
    ResponseEntity<ErrorApi> inesperado(Exception e) {
        log.error("Error no controlado", e);
        return respuesta(HttpStatus.INTERNAL_SERVER_ERROR, "ERROR_INTERNO", "Ocurrió un error inesperado.");
    }

    private static ResponseEntity<ErrorApi> respuesta(HttpStatus estado, String codigo, String mensaje) {
        return ResponseEntity.status(estado).body(new ErrorApi(codigo, mensaje));
    }
}
