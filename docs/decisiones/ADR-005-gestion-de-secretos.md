# ADR-005 — Gestión de secretos

- **Estado:** Aceptado (aprobado por Juan Pablo el 2026-11-11)
- **Fecha:** 2026-11-11

## Contexto

- Hoy todos los secretos (contraseñas de PostgreSQL y RabbitMQ y, desde la fase 1, la API key del LLM) viven en un `.env` en texto plano. Git lo ignora, pero queda legible en el disco y llega a los contenedores como **variables de entorno**.
- Las variables de entorno se filtran fácil: `docker inspect`, `/proc/<pid>/environ`, volcados de errores y procesos hijos que las heredan. Además, con un solo `.env`, cualquiera que lo lea tiene todos los secretos de todos los servicios.
- Hay varias máquinas (laptop con VM, PC de escritorio, servidor Rocky Linux) y los secretos tienen que llegar a todas.
- El despliegue pasa por tres etapas: compose en desarrollo, Podman en Rocky Linux y Kubernetes. Cada etapa tiene su mecanismo nativo de secretos.
- Cifrar el `.env` **no alcanza solo**: en algún momento hay que descifrarlo con una llave, y si el resultado vuelve a entrar como variables de entorno, el problema sigue. Lo que aísla de verdad es: que cada servicio reciba solo sus secretos, como archivos con permisos mínimos, y que se puedan rotar.

## Opciones evaluadas

| Opción | Encaje | Ventajas aquí | Desventajas aquí | Complejidad |
|---|---|---|---|---|
| `.env` en texto plano, ignorado por git (hoy) | Bajo | Simple | Legible en el disco, todo en variables de entorno, todos los secretos juntos, sin rotación | Baja |
| `.env` cifrado con SOPS + age | Medio | Se puede versionar cifrado y llevar entre máquinas; solo hace falta copiar una llave | Al descifrar vuelve a ser texto plano y variables de entorno. No aísla entre servicios | Baja |
| **Secretos como archivos montados, uno por servicio, con el mecanismo nativo de cada etapa** | **Alto** | Fuera de las variables de entorno (en `/run/secrets/`, solo lectura). Cada contenedor recibe solo los suyos. Es nativo en Compose, Podman (`podman secret`), systemd (`systemd-creds`, cifrado y atado a la máquina) y Kubernetes (Secrets). No agrega servicios | Hay que leer `*_FILE` o archivos en vez de variables. La rotación sigue siendo manual | Media |
| Servidor central de secretos (OpenBao o Vault) con credenciales dinámicas | Medio | El mejor aislamiento: contraseñas de BD de corta vida generadas por demanda, auditoría de cada lectura, rotación automática. Muy vistoso en el portafolio | Es un servicio más, con su propio arranque (unseal), respaldo y alta disponibilidad. Problema del huevo y la gallina: hace falta un secreto para pedir los secretos. Pesado para 1 persona antes de Kubernetes | Alta |

## Recomendación

**Archivos montados por servicio desde ya, SOPS + age para guardar y mover los secretos entre máquinas, y OpenBao como paso opcional en la fase 9.**

| Etapa | Cómo llegan los secretos |
|---|---|
| Desarrollo (compose) | Bloque `secrets:` de Compose. Cada servicio declara solo los secretos que necesita y los lee de `/run/secrets/<nombre>` (o con las variables `*_FILE`, como `POSTGRES_PASSWORD_FILE`). La carpeta `secrets/` está fuera de git |
| Desarrollo del agente (fase 1, consola) | `dotnet user-secrets` para la API key del LLM: queda fuera de la carpeta del proyecto y nunca en un archivo del repo |
| Guardar y mover secretos | Un `secrets.enc.yaml` cifrado con SOPS + age, que sí se versiona. La llave privada de age vive solo en cada máquina (`~/.config/sops/age/keys.txt`, permisos `600`) y nunca en el repo |
| Rocky Linux (fase 8) | `podman secret` o credenciales cifradas de systemd (`systemd-creds encrypt`, atadas a la máquina y, si hay, al TPM). Ningún `.env` en texto plano en el servidor |
| Kubernetes (fase 9) | Secrets con cifrado en reposo activado, generados desde SOPS. Opcional, con su propio ADR: OpenBao con credenciales dinámicas de PostgreSQL |

Reglas que aplican en todas las etapas:
- Cada servicio recibe solo sus secretos. El `agente` nunca ve la contraseña de administrador ni la de `mesa-servicio`.
- Ningún secreto en variables de entorno, en la imagen, en logs ni en mensajes de error.
- La configuración que no es secreta (modelo, límites, umbrales) sigue en variables de entorno.
- Se documenta cómo rotar cada secreto.

## Qué tendría que cambiar para que otra opción fuera mejor

- Si el proyecto pasara a un equipo de varias personas o a producción real, el servidor central (OpenBao o Vault) dejaría de ser opcional: la auditoría y la rotación automática valen su costo.
- Si solo hubiera una máquina y nunca se desplegara, el `.env` ignorado por git sería suficiente.

## Consecuencias

- `compose.yml` y `deploy/postgres/init/01-esquemas-y-usuarios.sh` cambian para leer los secretos como archivos. `.env` se queda solo con la configuración que no es secreta.
- Se agregan `secrets/` al `.gitignore` y un script que genera los secretos locales (o los descifra con SOPS).
- Los servicios Java y .NET leen las contraseñas desde archivos: Spring con `spring.config.import=configtree:/run/secrets/`, .NET con `AddKeyPerFile("/run/secrets")`.
- Nueva herramienta de desarrollo: SOPS + age.
- Se agrega a la suite adversaria: ningún secreto aparece en `docker inspect`, en `/proc/<pid>/environ` ni en los logs.
