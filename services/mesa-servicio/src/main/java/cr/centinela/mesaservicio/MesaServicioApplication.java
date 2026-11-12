package cr.centinela.mesaservicio;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;

/**
 * Monolito modular (ADR-003). Módulos: plataforma (seguridad y errores, compartido), tickets, sla y
 * auditoria. Cada módulo expone solo su paquete api/; el resto es interno (lo verifica ModulosTest).
 */
@SpringBootApplication
@ConfigurationPropertiesScan
public class MesaServicioApplication {

    public static void main(String[] args) {
        SpringApplication.run(MesaServicioApplication.class, args);
    }
}
