#!/bin/bash
# Secretos de desarrollo (ADR-005). Cada secreto es un archivo en secrets/ (fuera de git)
# que compose monta solo en el contenedor que lo necesita, en /run/secrets/.
#
#   ./scripts/secretos.sh generar    crea los secretos que falten con valores aleatorios (no pisa los existentes)
#   ./scripts/secretos.sh llave      crea tu llave de age (si no existe) y la registra en .sops.yaml
#   ./scripts/secretos.sh cifrar     guarda secrets/ cifrado en secrets.enc.env (este archivo sí se versiona)
#   ./scripts/secretos.sh descifrar  recrea secrets/ desde secrets.enc.env (por ejemplo, en otra máquina)
#   ./scripts/secretos.sh guardar N  pide sin mostrarlo un secreto externo (por ejemplo voyage_api_key) y lo guarda
set -euo pipefail
cd "$(dirname "$0")/.."

DIR=secrets
CIFRADO=secrets.enc.env
NOMBRES=(
  postgres_admin_password
  mesa_servicio_db_password
  mesa_servicio_app_db_password
  agente_db_password
  powerbi_ro_db_password
  rabbitmq_password
  rabbitmq_mesa_servicio_password
  rabbitmq_agente_password
  usuarios_prueba_password
)
# Secretos que no se pueden generar (los entrega un proveedor). Son opcionales: sin ellos
# la aplicación arranca igual y usa su camino de respaldo (ADR-007).
EXTERNOS=(
  voyage_api_key
)
PLANTILLA_RABBITMQ=deploy/rabbitmq/definiciones.plantilla.json

aleatorio() { openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | cut -c1-32; }

preparar_dir() {
  mkdir -p "$DIR"
  # La carpeta protege los secretos en el host. Los archivos quedan legibles (644) porque
  # compose los monta tal cual y cada contenedor los lee con su propio usuario.
  chmod 700 "$DIR"
}

escribir() { # nombre valor
  (umask 022 && printf '%s' "$2" > "$DIR/$1")
}

# Hash de contraseña de RabbitMQ (rabbit_password_hashing_sha256): base64(sal de 4 bytes + SHA-256(sal + clave)).
# Así el archivo de definiciones nunca lleva una contraseña en claro.
hash_rabbitmq() { # archivo-con-la-clave
  local tmp; tmp=$(mktemp -d)
  openssl rand 4 > "$tmp/sal"
  cat "$tmp/sal" "$1" | openssl dgst -sha256 -binary > "$tmp/resumen"
  cat "$tmp/sal" "$tmp/resumen" | openssl base64 -A
  rm -rf "$tmp"
}

definiciones_rabbitmq() {
  # A3: un usuario por servicio con permisos mínimos; colas, DLQ y enlaces declarados aquí, no por los servicios.
  local n
  for n in rabbitmq_password rabbitmq_mesa_servicio_password rabbitmq_agente_password; do
    [ -s "$DIR/$n" ] || { echo "Falta $DIR/$n: corre './scripts/secretos.sh generar' para crear las definiciones de RabbitMQ." >&2; return 0; }
  done
  (umask 022 && sed \
    -e "s|__HASH_ADMIN__|$(hash_rabbitmq "$DIR/rabbitmq_password")|" \
    -e "s|__HASH_MESA_SERVICIO__|$(hash_rabbitmq "$DIR/rabbitmq_mesa_servicio_password")|" \
    -e "s|__HASH_AGENTE__|$(hash_rabbitmq "$DIR/rabbitmq_agente_password")|" \
    "$PLANTILLA_RABBITMQ" > "$DIR/rabbitmq-definiciones.json")
  # El formato anterior llevaba la contraseña del administrador en claro.
  rm -f "$DIR/rabbitmq.conf"
}

ruta_llave() {
  if [ -n "${SOPS_AGE_KEY_FILE:-}" ]; then echo "$SOPS_AGE_KEY_FILE"
  elif [ -n "${APPDATA:-}" ]; then echo "$(cygpath -u "$APPDATA" 2>/dev/null || echo "$APPDATA")/sops/age/keys.txt"
  else echo "${XDG_CONFIG_HOME:-$HOME/.config}/sops/age/keys.txt"
  fi
}

generar() {
  preparar_dir
  local creados=0
  for n in "${NOMBRES[@]}"; do
    if [ ! -s "$DIR/$n" ]; then escribir "$n" "$(aleatorio)"; creados=$((creados + 1)); fi
  done
  definiciones_rabbitmq
  echo "Secretos listos en $DIR/ ($creados nuevos)."
}

llave() {
  local archivo; archivo=$(ruta_llave)
  if [ ! -s "$archivo" ]; then
    mkdir -p "$(dirname "$archivo")"
    (umask 077 && age-keygen -o "$archivo" 2>/dev/null)
    echo "Llave creada en $archivo. Respáldala fuera de esta máquina: sin ella no se puede descifrar."
  fi
  local publica; publica=$(age-keygen -y "$archivo")
  if [ -f .sops.yaml ] && grep -q "$publica" .sops.yaml; then
    echo "Tu llave ya está registrada en .sops.yaml."
  elif [ -f .sops.yaml ]; then
    echo "Agrega esta llave pública a la lista 'age' de .sops.yaml y corre 'sops updatekeys $CIFRADO' desde una máquina que ya pueda descifrar:"
    echo "  $publica"
  else
    printf 'creation_rules:\n  - path_regex: secrets\\.enc\\.env$\n    age: >-\n      %s\n' "$publica" > .sops.yaml
    echo ".sops.yaml creado con tu llave pública."
  fi
}

cifrar() {
  [ -f .sops.yaml ] || { echo "Falta .sops.yaml: corre primero './scripts/secretos.sh llave'." >&2; exit 1; }
  local plano="$DIR/.plano.env"
  # Comillas dobles: la ruta se fija ahora, porque al salir la variable local ya no existe.
  trap "rm -f '$plano'" EXIT
  # La redirección va dentro del subshell para que el archivo plano nazca ya con umask 077.
  (umask 077 && {
    for n in "${NOMBRES[@]}"; do
      [ -s "$DIR/$n" ] || { echo "Falta $DIR/$n: corre './scripts/secretos.sh generar'." >&2; exit 1; }
      echo "$n=$(cat "$DIR/$n")"
    done
    for n in "${EXTERNOS[@]}"; do
      if [ -s "$DIR/$n" ]; then echo "$n=$(cat "$DIR/$n")"; fi
    done
  } > "$plano")
  sops encrypt --filename-override "$CIFRADO" --input-type dotenv --output-type dotenv "$plano" > "$CIFRADO"
  echo "Cifrado en $CIFRADO. Este archivo sí se puede versionar."
}

descifrar() {
  [ -f "$CIFRADO" ] || { echo "No existe $CIFRADO." >&2; exit 1; }
  preparar_dir
  local clave valor
  while IFS='=' read -r clave valor; do
    [ -z "$clave" ] && continue
    if printf '%s\n' "${NOMBRES[@]}" "${EXTERNOS[@]}" | grep -qx "$clave"; then escribir "$clave" "$valor"
    else echo "Ignorando clave desconocida: $clave" >&2
    fi
  done < <(sops decrypt --input-type dotenv --output-type dotenv "$CIFRADO")
  definiciones_rabbitmq
  echo "Secretos recreados en $DIR/."
}

guardar() { # nombre
  local nombre=${1:-}
  printf '%s\n' "${EXTERNOS[@]}" | grep -qx -- "$nombre" \
    || { echo "Uso: $0 guardar <nombre>. Nombres válidos: ${EXTERNOS[*]}" >&2; exit 1; }
  [ -t 0 ] || { echo "Corre este comando en una terminal: el valor se escribe a mano, nunca por argumento ni tubería." >&2; exit 1; }
  local valor
  # -s: no se muestra al escribirlo ni queda en el historial de la terminal.
  IFS= read -rs -p "Pega el valor de $nombre y presiona Enter (no se verá): " valor; echo
  valor=$(printf '%s' "$valor" | tr -d '[:space:]')
  [ -n "$valor" ] || { echo "Valor vacío: no se guardó nada." >&2; exit 1; }
  preparar_dir
  escribir "$nombre" "$valor"
  echo "Guardado en $DIR/$nombre (${#valor} caracteres). Corre './scripts/secretos.sh cifrar' para respaldarlo cifrado."
}

case "${1:-}" in
  generar) generar ;;
  llave) llave ;;
  cifrar) cifrar ;;
  descifrar) descifrar ;;
  guardar) guardar "${2:-}" ;;
  *) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
esac
