package cr.centinela.mesaservicio.plataforma;

import java.util.List;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.autoconfigure.condition.ConditionalOnWebApplication;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.context.annotation.Profile;
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

    @Bean
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
