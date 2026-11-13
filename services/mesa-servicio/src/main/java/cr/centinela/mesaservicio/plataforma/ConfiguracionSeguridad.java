package cr.centinela.mesaservicio.plataforma;

import java.util.List;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.autoconfigure.condition.ConditionalOnWebApplication;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
import org.springframework.core.annotation.Order;
import org.springframework.http.HttpMethod;
import org.springframework.security.config.Customizer;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.crypto.factory.PasswordEncoderFactories;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.security.provisioning.InMemoryUserDetailsManager;
import org.springframework.security.web.SecurityFilterChain;

/**
 * Seguridad de la fase 2: usuarios de prueba con HTTP Basic, sin sesión. En la fase 4 se reemplaza por el
 * BFF con Keycloak de ADR-004 (cookie de sesión con CSRF). Hoy CSRF no aplica porque no hay cookies: cada
 * petición trae sus credenciales, así que un sitio ajeno no puede usarlas en nombre del usuario.
 */
@Configuration
// Con el perfil "migracion" no hay servidor web: sin esto, el proceso de migración terminaba con error (exit 1)
// aunque Flyway ya hubiera migrado, y un Job de Kubernetes lo daría por fallido.
@ConditionalOnWebApplication
class ConfiguracionSeguridad {

    public static final String ROL_SERVICIO_AGENTE = "SERVICIO_AGENTE";

    /**
     * API interna (/interna/**): solo lectura y solo para la cuenta de servicio del agente, con su propio almacén
     * de usuarios. Ninguna persona entra aquí, y la cuenta del agente no sirve en /api. nginx no publica esta
     * ruta: solo se alcanza desde la red interna. La contraseña llega como secreto (api_interna_agente_password);
     * sin ella la API interna queda cerrada.
     */
    @Bean
    @Order(1)
    SecurityFilterChain filtrosInternos(HttpSecurity http, CuentaServicioAgente cuenta, PasswordEncoder codificador) throws Exception {
        var usuarios = cuenta.password() == null || cuenta.password().isBlank()
                ? new InMemoryUserDetailsManager()
                : new InMemoryUserDetailsManager(User.withUsername(cuenta.usuario())
                        .password(codificador.encode(cuenta.password()))
                        .roles(ROL_SERVICIO_AGENTE)
                        .build());
        return http
                .securityMatcher("/interna/**")
                .authorizeHttpRequests(a -> a
                        .requestMatchers(HttpMethod.GET, "/interna/**").hasRole(ROL_SERVICIO_AGENTE)
                        .anyRequest().denyAll())
                .userDetailsService(usuarios)
                .httpBasic(Customizer.withDefaults())
                .sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .csrf(c -> c.disable())
                .build();
    }

    @ConfigurationProperties("centinela.api-interna")
    record CuentaServicioAgente(String usuario, String password) {}

    @Bean
    @Order(2)
    SecurityFilterChain filtros(HttpSecurity http) throws Exception {
        return http
                .authorizeHttpRequests(a -> a
                        .requestMatchers("/actuator/health/**").permitAll()
                        .requestMatchers("/api/**").authenticated()
                        .anyRequest().denyAll())
                .httpBasic(Customizer.withDefaults())
                .sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .csrf(c -> c.disable())
                .build();
    }

    @Bean
    PasswordEncoder codificador() {
        return PasswordEncoderFactories.createDelegatingPasswordEncoder();
    }

    /**
     * Usuarios de prueba, solo con el perfil "desarrollo". Todos comparten una contraseña que llega como
     * secreto (secrets/usuarios_prueba_password, ADR-005), nunca escrita en el repositorio. Sin ese perfil
     * no hay usuarios: todo queda cerrado hasta que llegue Keycloak.
     */
    @Bean
    @Profile("desarrollo")
    UserDetailsService usuariosDePrueba(UsuariosDePrueba config, PasswordEncoder codificador) {
        var usuarios = config.usuarios().stream()
                .map(u -> User.withUsername(u.nombre())
                        .password(codificador.encode(config.password()))
                        .roles(u.roles().stream().map(Enum::name).toArray(String[]::new))
                        .build())
                .toList();
        return new InMemoryUserDetailsManager(usuarios);
    }

    @Bean
    @Profile("!desarrollo")
    UserDetailsService sinUsuarios() {
        return new InMemoryUserDetailsManager();
    }

    @ConfigurationProperties("centinela.usuarios-prueba")
    record UsuariosDePrueba(String password, List<UsuarioDePrueba> usuarios) {
        record UsuarioDePrueba(String nombre, List<Rol> roles) {}
    }
}
