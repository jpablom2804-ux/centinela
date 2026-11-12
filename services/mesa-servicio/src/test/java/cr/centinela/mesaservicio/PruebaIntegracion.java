package cr.centinela.mesaservicio;

import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.concurrent.atomic.AtomicReference;
import org.flywaydb.core.Flyway;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Primary;
import org.springframework.context.annotation.Import;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.testcontainers.images.builder.Transferable;
import org.testcontainers.postgresql.PostgreSQLContainer;
import org.testcontainers.rabbitmq.RabbitMQContainer;
import org.testcontainers.utility.DockerImageName;
import org.testcontainers.utility.MountableFile;

/**
 * PostgreSQL real con el mismo script de init del proyecto (roles y esquemas por servicio), Flyway con el
 * rol dueño, y la aplicación conectada con el rol mesa_servicio_app, igual que en producción (A14).
 */
@SpringBootTest
@ActiveProfiles("desarrollo")
@Import(PruebaIntegracion.Reloj.class)
public abstract class PruebaIntegracion {

    public static final String CLAVE_USUARIOS = "clave-de-prueba";
    public static final String CLAVE_APP = "clave-app-prueba";
    public static final String CLAVE_DUENO = "clave-dueno-prueba";

    protected static final PostgreSQLContainer POSTGRES = new PostgreSQLContainer(
                    DockerImageName.parse("docker.io/pgvector/pgvector:pg18").asCompatibleSubstituteFor("postgres"))
            .withDatabaseName("centinela")
            .withUsername("centinela_admin")
            .withPassword("clave-admin-prueba")
            .withCopyFileToContainer(
                    MountableFile.forHostPath(Path.of("../../deploy/postgres/init/01-esquemas-y-usuarios.sh"), 0755),
                    "/docker-entrypoint-initdb.d/01-esquemas-y-usuarios.sh")
            .withCopyToContainer(Transferable.of(CLAVE_DUENO), "/run/secrets/mesa_servicio_db_password")
            .withCopyToContainer(Transferable.of(CLAVE_APP), "/run/secrets/mesa_servicio_app_db_password")
            .withCopyToContainer(Transferable.of("clave-agente-prueba"), "/run/secrets/agente_db_password")
            .withCopyToContainer(Transferable.of("clave-powerbi-prueba"), "/run/secrets/powerbi_ro_db_password");

    /** El broker real: el relevo del outbox necesita confirmaciones y mensajes devueltos de verdad. */
    protected static final RabbitMQContainer RABBIT = new RabbitMQContainer(DockerImageName.parse("docker.io/library/rabbitmq:4-management").asCompatibleSubstituteFor("rabbitmq"));

    static {
        RABBIT.start();
        POSTGRES.start();
        // Las migraciones corren aparte y con el rol dueño, como en el perfil "migracion".
        Flyway.configure()
                .dataSource(POSTGRES.getJdbcUrl(), "mesa_servicio", CLAVE_DUENO)
                .defaultSchema("mesa_servicio")
                .schemas("mesa_servicio", "auditoria", "reportes")
                .createSchemas(false)
                .load()
                .migrate();
    }

    @DynamicPropertySource
    static void propiedades(DynamicPropertyRegistry registro) {
        registro.add("spring.datasource.url", POSTGRES::getJdbcUrl);
        registro.add("spring.datasource.username", () -> "mesa_servicio_app");
        registro.add("spring.datasource.password", () -> CLAVE_APP);
        registro.add("centinela.usuarios-prueba.password", () -> CLAVE_USUARIOS);
        // El vigilante de SLA se llama a mano en las pruebas; la tarea programada no debe interferir.
        registro.add("centinela.sla.revision", () -> "PT24H");
        registro.add("spring.rabbitmq.host", RABBIT::getHost);
        registro.add("spring.rabbitmq.port", RABBIT::getAmqpPort);
        registro.add("spring.rabbitmq.username", RABBIT::getAdminUsername);
        registro.add("spring.rabbitmq.password", RABBIT::getAdminPassword);
        registro.add("spring.rabbitmq.virtual-host", () -> "/");
        // Igual que el vigilante: el relevo del outbox se llama a mano.
        registro.add("centinela.mensajeria.relevo", () -> "PT24H");
    }

    /** Reloj que las pruebas pueden mover, para probar el SLA sin esperar horas. */
    @TestConfiguration
    public static class Reloj {

        @Bean
        @Primary
        RelojMovible relojMovible() {
            return new RelojMovible(Instant.parse("2026-11-09T15:00:00Z")); // lunes 09:00 en Costa Rica
        }
    }

    public static final class RelojMovible extends Clock {

        private final AtomicReference<Instant> ahora;

        RelojMovible(Instant inicio) {
            this.ahora = new AtomicReference<>(inicio);
        }

        public void fijar(Instant instante) {
            ahora.set(instante);
        }

        public void avanzar(java.time.Duration duracion) {
            ahora.updateAndGet(i -> i.plus(duracion));
        }

        @Override
        public Instant instant() {
            return ahora.get();
        }

        @Override
        public ZoneId getZone() {
            return ZoneOffset.UTC;
        }

        @Override
        public Clock withZone(ZoneId zona) {
            return this;
        }
    }
}
