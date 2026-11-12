package cr.centinela.mesaservicio.sla.internal;

import javax.sql.DataSource;
import net.javacrumbs.shedlock.core.LockProvider;
import net.javacrumbs.shedlock.provider.jdbctemplate.JdbcTemplateLockProvider;
import net.javacrumbs.shedlock.spring.annotation.EnableSchedulerLock;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.scheduling.annotation.EnableScheduling;

/** Tareas programadas con bloqueo en la base (tabla mesa_servicio.shedlock). Apagadas al migrar. */
@Configuration
@Profile("!migracion")
@EnableScheduling
@EnableSchedulerLock(defaultLockAtMostFor = "PT5M")
class ConfiguracionSla {

    @Bean
    LockProvider bloqueos(DataSource datos) {
        return new JdbcTemplateLockProvider(JdbcTemplateLockProvider.Configuration.builder()
                .withJdbcTemplate(new JdbcTemplate(datos))
                .withTableName("mesa_servicio.shedlock")
                .usingDbTime()
                .build());
    }
}
