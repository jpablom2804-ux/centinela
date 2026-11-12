package cr.centinela.mesaservicio;

import org.junit.jupiter.api.Test;
import org.springframework.modulith.core.ApplicationModules;

/** Hace cumplir el monolito modular (ADR-003): ningún módulo usa el internal/ de otro ni hay ciclos. */
class ModulosTest {

    @Test
    void los_modulos_solo_se_usan_por_su_api() {
        ApplicationModules.of(MesaServicioApplication.class).verify();
    }
}
