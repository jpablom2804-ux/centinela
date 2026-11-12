package cr.centinela.mesaservicio.plataforma.mensajeria;

import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * @param relevo             cada cuánto se buscan eventos pendientes
 * @param lote               cuántos se toman por vuelta (con SKIP LOCKED, cada réplica toma los suyos)
 * @param maxIntentos        después de esto el evento queda sin publicar y se alerta: nunca reintentos infinitos
 * @param esperaConfirmacion cuánto se espera el ack del broker antes de darlo por fallido
 * @param maxEspera          tope del backoff exponencial entre intentos
 */
@ConfigurationProperties("centinela.mensajeria")
record PropiedadesMensajeria(Duration relevo, int lote, int maxIntentos, Duration esperaConfirmacion, Duration maxEspera) {}
