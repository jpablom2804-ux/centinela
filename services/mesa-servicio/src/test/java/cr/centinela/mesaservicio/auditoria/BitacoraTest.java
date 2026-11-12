package cr.centinela.mesaservicio.auditoria;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import cr.centinela.mesaservicio.PruebaIntegracion;
import java.sql.DriverManager;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.dao.DataAccessException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.support.TransactionTemplate;

/** La bitácora es de solo inserción para la app, y la cadena de hashes detecta alteraciones. */
class BitacoraTest extends PruebaIntegracion {

    @Autowired JdbcTemplate jdbcApp; // conectado como mesa_servicio_app
    @Autowired RegistroAuditoria auditoria;
    @Autowired TransactionTemplate tx;

    private void registrar(String accion) {
        tx.executeWithoutResult(s -> auditoria.registrar(new EventoAuditoria("prueba", accion, "prueba", "1", null, Map.of("x", 1))));
    }

    @Test
    void la_app_no_puede_modificar_ni_borrar_la_bitacora() {
        registrar("PRUEBA_PERMISOS");

        assertThatThrownBy(() -> jdbcApp.update("update auditoria.bitacora set actor = 'otro'")).isInstanceOf(DataAccessException.class).rootCause().hasMessageContaining("permission denied");
        assertThatThrownBy(() -> jdbcApp.update("delete from auditoria.bitacora")).isInstanceOf(DataAccessException.class).rootCause().hasMessageContaining("permission denied");
        assertThatThrownBy(() -> jdbcApp.execute("truncate auditoria.bitacora")).isInstanceOf(DataAccessException.class).rootCause().hasMessageContaining("permission denied");
    }

    @Test
    void registrar_fuera_de_una_transaccion_falla() {
        // La acción y su registro van juntos: sin transacción no hay registro suelto.
        assertThatThrownBy(() -> auditoria.registrar(new EventoAuditoria("prueba", "SUELTO", "prueba", "1", null, null)))
                .isInstanceOf(org.springframework.transaction.IllegalTransactionStateException.class);
    }

    @Test
    void la_cadena_detecta_un_registro_alterado_por_un_superusuario() throws Exception {
        registrar("PRUEBA_CADENA_1");
        registrar("PRUEBA_CADENA_2");
        assertThat(auditoria.primerRegistroAlterado()).isNull();

        // Un administrador de la base sí puede alterar la tabla; la cadena lo deja en evidencia (A8).
        long alterado;
        try (var admin = DriverManager.getConnection(POSTGRES.getJdbcUrl(), POSTGRES.getUsername(), POSTGRES.getPassword());
                var st = admin.createStatement()) {
            var rs = st.executeQuery("select id from auditoria.bitacora where accion = 'PRUEBA_CADENA_1' order by id desc limit 1");
            rs.next();
            alterado = rs.getLong(1);
            st.executeUpdate("update auditoria.bitacora set actor = 'intruso' where id = " + alterado);
        }

        assertThat(auditoria.primerRegistroAlterado()).isEqualTo(alterado);

        // Se deja la tabla íntegra para las demás pruebas: se vuelve al valor original.
        try (var admin = DriverManager.getConnection(POSTGRES.getJdbcUrl(), POSTGRES.getUsername(), POSTGRES.getPassword());
                var st = admin.createStatement()) {
            st.executeUpdate("update auditoria.bitacora set actor = 'prueba' where id = " + alterado);
        }
        assertThat(auditoria.primerRegistroAlterado()).isNull();
    }
}
