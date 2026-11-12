package cr.centinela.mesaservicio.plataforma.mensajeria;

import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.scheduling.annotation.EnableScheduling;

/** El relevo del outbox es una tarea programada; apagada al migrar, como el vigilante de SLA. */
@Configuration
@Profile("!migracion")
@EnableScheduling
class ConfiguracionMensajeria {}
