# generador

Herramienta de desarrollo, no es parte del producto (ADR-003). Crea tickets sintéticos por la API de `mesa-servicio`, igual que una persona, así pasan por las mismas validaciones, permisos, límites y bitácora. Nunca toca la base de datos y no tiene usuario de base.

Los casos están inspirados en tipos de casos reales de soporte de TI en un banco (VPN, cuentas bloqueadas, phishing, core bancario, SINPE, app móvil…), sin ningún dato personal. Una prueba verifica que ningún texto se parezca a una cédula, una cuenta o un correo.

```bash
cd tools/generador
./mvnw -B -DskipTests package
export CENTINELA_SECRETOS="$(cd ../../secrets && pwd)"   # en Git Bash: "$(cygpath -m "$(cd ../../secrets && pwd)")"
java -jar target/generador-0.0.1-SNAPSHOT.jar --cantidad 30 --semilla 42
```

| Opción | Por defecto | Para qué |
|---|---|---|
| `--cantidad` | 20 | Tickets a crear (1 a 500) |
| `--url` | `http://127.0.0.1:8080` | Dónde corre `mesa-servicio` |
| `--semilla` | aleatoria | La misma semilla genera la misma secuencia |
| `--usuarios` | `solicitante1,solicitante2` | Se reparten los tickets en orden |

La contraseña de los usuarios de prueba se lee de `secrets/usuarios_prueba_password` y nunca se pasa como argumento, para que no quede en el historial de la terminal ni en la lista de procesos. Si un usuario llega al límite de 20 tickets por hora (A5), el generador deja de enviar con ese usuario y sigue con los demás.
