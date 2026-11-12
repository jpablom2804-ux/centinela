# ADR-004 — Autenticación, sesiones y cookies

- **Estado:** Aceptado (aprobado por Juan Pablo el 2026-11-11)
- **Fecha:** 2026-11-11

## Contexto

- El perfil ya fija OIDC con Keycloak y prohíbe el login hecho a mano. Falta decidir **dónde viven los tokens** y cómo se protegen las sesiones.
- Es una mesa de servicio para entidades financieras: hay roles con poder real (aprobar cambios, auditar). Un token robado o una sesión secuestrada es un incidente.
- Riesgos que hay que cubrir: fuerza bruta de contraseñas, enumeración de usuarios por el mensaje de error o por el **tiempo de respuesta** (milisegundos de diferencia entre "usuario no existe" y "contraseña incorrecta"), robo de tokens por XSS, CSRF y fijación de sesión.
- `mesa-servicio` tendrá varias réplicas detrás de un balanceador (fases 8 y 9): la sesión no puede depender de a qué réplica cae cada petición.
- En el proyecto anterior (scoopy) se usó JWT en el header, con CSRF desactivado y login propio. Aquí no aplica: hay Keycloak, y en el navegador no debería quedar ningún token.

## Opciones evaluadas

| Opción | Encaje | Ventajas aquí | Desventajas aquí | Complejidad |
|---|---|---|---|---|
| SPA como cliente público (PKCE), tokens en memoria del navegador | Medio | Lo más común en tutoriales; el backend solo valida JWT | El access token vive en JavaScript: un XSS lo roba. Refrescar sin `localStorage` es frágil. Cada pestaña maneja tokens | Media |
| SPA con tokens en `localStorage` | Bajo | Simple | Prohibido por el skill: cualquier XSS se lleva la sesión | Baja |
| **BFF: `mesa-servicio` es el cliente OIDC, el navegador solo tiene una cookie de sesión** | **Alto** | Ningún token llega al navegador. Cookie `HttpOnly` + `Secure` + `SameSite`, CSRF con Spring Security, cierre de sesión real desde el servidor. Es lo que recomienda el borrador de OAuth para apps de navegador | Sesión con estado en el servidor: hay que compartirla entre réplicas (Spring Session JDBC). Hay que configurar CSRF para la SPA | Media |

## Recomendación

**BFF con Spring Security (`oauth2Login`) en `mesa-servicio` y sesión compartida en PostgreSQL con Spring Session JDBC.** El frontend nunca ve un token; habla con `/api` usando la cookie de sesión y el token CSRF.

### Cookies
- Nombre `__Host-SESSION`: el prefijo obliga a `Secure`, `Path=/` y sin `Domain`, así que no se comparte con subdominios.
- `HttpOnly`, `Secure`, `SameSite=Lax`. `Lax` y no `Strict`, porque `Strict` rompe el regreso desde Keycloak después del login.
- CSRF: `CookieCsrfTokenRepository` (cookie `XSRF-TOKEN` legible por la SPA) y header `X-XSRF-TOKEN` en toda petición que modifica algo.
- Expiración por inactividad de 15 minutos y absoluta de 8 horas (un turno). Los mismos valores en la sesión SSO de Keycloak.
- Nuevo ID de sesión al iniciar sesión (contra fijación de sesión). Cerrar sesión invalida la sesión local y la de Keycloak (RP-initiated logout).

### Login en Keycloak
- **Detección de fuerza bruta** con bloqueo **temporal**: 5 fallos, espera creciente desde 1 minuto hasta un máximo de 15, y reinicio del contador a las 12 horas. El bloqueo permanente queda apagado, porque permitiría a un atacante bloquear a usuarios legítimos a propósito.
- **Rate limiting en nginx** sobre el endpoint de login de Keycloak y sobre `/api`, por IP, respondiendo `429`.
- **Mensajes y tiempos iguales:** el mismo mensaje para "usuario no existe" y "contraseña incorrecta", y lo mismo en "olvidé mi contraseña". La diferencia de tiempos se mide en la suite adversaria.
- **Política de contraseñas:** mínimo 12 caracteres, distinta del usuario y sin repetir las últimas 5. Registro abierto desactivado.
- **MFA (OTP) obligatorio** para `coordinador`, `aprobador_cambios` y `auditor`.
- La consola de administración de Keycloak no se publica: solo se entra por localhost o por un túnel SSH.

## Qué tendría que cambiar para que otra opción fuera mejor

Si apareciera un cliente que no es navegador (por ejemplo una app móvil u otro servicio que consuma la API), ese cliente usaría tokens con PKCE o credenciales de cliente. El BFF se mantiene para el navegador.

## Consecuencias

- Keycloak necesita su propia base de datos (`keycloak`) en la instancia de PostgreSQL, con su propio usuario, separada de `centinela`.
- `mesa-servicio` guarda las sesiones en su esquema (tablas de Spring Session), así que cualquier réplica atiende cualquier petición. No se usan sesiones pegajosas (sticky sessions).
- En `/api`, los errores no revelan existencia: un ticket ajeno y uno inexistente responden igual (`404`) y siguen el mismo camino de código.
- Se agregan casos de login a la suite adversaria (`docs/SEGURIDAD.md`).
