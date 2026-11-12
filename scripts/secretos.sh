#!/bin/bash
# Secretos de desarrollo (ADR-005). Cada secreto es un archivo en secrets/ (fuera de git)
# que compose monta solo en el contenedor que lo necesita, en /run/secrets/.
#
#   ./scripts/secretos.sh generar    crea los secretos que falten con valores aleatorios (no pisa los existentes)
#   ./scripts/secretos.sh llave      crea tu llave de age (si no existe) y la registra en .sops.yaml
#   ./scripts/secretos.sh cifrar     guarda secrets/ cifrado en secrets.enc.env (este archivo sí se versiona)
#   ./scripts/secretos.sh descifrar  recrea secrets/ desde secrets.enc.env (por ejemplo, en otra máquina)
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
  usuarios_prueba_password
)
RABBITMQ_USUARIO=${RABBITMQ_USUARIO:-centinela}

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

conf_rabbitmq() {
  # RabbitMQ no lee la contraseña desde un archivo propio: se la damos en un archivo de
  # configuración que compose monta en /etc/rabbitmq/conf.d/.
  (umask 022 && {
    echo "default_user = $RABBITMQ_USUARIO"
    echo "default_pass = $(cat "$DIR/rabbitmq_password")"
    echo "default_vhost = centinela"
  } > "$DIR/rabbitmq.conf")
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
  conf_rabbitmq
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
  (umask 077 && for n in "${NOMBRES[@]}"; do
    [ -s "$DIR/$n" ] || { echo "Falta $DIR/$n: corre './scripts/secretos.sh generar'." >&2; exit 1; }
    echo "$n=$(cat "$DIR/$n")"
  done > "$plano")
  sops encrypt --filename-override "$CIFRADO" --input-type dotenv --output-type dotenv "$plano" > "$CIFRADO"
  echo "Cifrado en $CIFRADO. Este archivo sí se puede versionar."
}

descifrar() {
  [ -f "$CIFRADO" ] || { echo "No existe $CIFRADO." >&2; exit 1; }
  preparar_dir
  local clave valor
  while IFS='=' read -r clave valor; do
    [ -z "$clave" ] && continue
    if printf '%s\n' "${NOMBRES[@]}" | grep -qx "$clave"; then escribir "$clave" "$valor"
    else echo "Ignorando clave desconocida: $clave" >&2
    fi
  done < <(sops decrypt --input-type dotenv --output-type dotenv "$CIFRADO")
  conf_rabbitmq
  echo "Secretos recreados en $DIR/."
}

case "${1:-}" in
  generar) generar ;;
  llave) llave ;;
  cifrar) cifrar ;;
  descifrar) descifrar ;;
  *) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
esac
