package cr.centinela.mesaservicio.plataforma;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import org.slf4j.MDC;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

/** Pone el ID de correlación en el MDC antes que todo lo demás (incluida la seguridad) y lo devuelve al cliente. */
@Component
@Order(Ordered.HIGHEST_PRECEDENCE)
class FiltroCorrelacion extends OncePerRequestFilter {

    @Override
    protected void doFilterInternal(HttpServletRequest peticion, HttpServletResponse respuesta, FilterChain cadena)
            throws ServletException, IOException {
        var id = Correlacion.desdeCabecera(peticion.getHeader(Correlacion.CABECERA));
        MDC.put(Correlacion.CLAVE_MDC, id);
        respuesta.setHeader(Correlacion.CABECERA, id);
        try {
            cadena.doFilter(peticion, respuesta);
        } finally {
            MDC.remove(Correlacion.CLAVE_MDC);
        }
    }
}
