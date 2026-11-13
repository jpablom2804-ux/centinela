package cr.centinela.mesaservicio.plataforma;

import java.time.Clock;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Un solo reloj en UTC para todos los módulos; las pruebas lo reemplazan por uno que se puede mover. */
@Configuration
class ConfiguracionReloj {

    @Bean
    Clock reloj() {
        return Clock.systemUTC();
    }
}
