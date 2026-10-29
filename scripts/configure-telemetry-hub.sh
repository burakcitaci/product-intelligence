#!/usr/bin/env bash
set -euo pipefail

kube_context="${KUBE_CONTEXT:-kind-observability}"
namespace="${NAMESPACE:-telemetry-hub}"
organization="${ORGANIZATION:-demo}"
team="${TEAM:-backend}"
environment="${ENVIRONMENT:-development}"
valid_days="${VALID_DAYS:-365}"

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/.." && pwd)"
env_path="$project_root/.env"

for command_name in kubectl jq openssl base64 python3; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "Required command '$command_name' was not found." >&2
    exit 1
  }
done
[[ -f "$env_path" ]] || { echo "ProductHub .env was not found." >&2; exit 1; }

encoded_registry="$(kubectl --context "$kube_context" -n "$namespace" get secret telemetry-ingestion -o 'jsonpath={.data.INGESTION_KEYS_JSON}')"
registry_json="$(printf '%s' "$encoded_registry" | base64 --decode)"
raw_key="$(openssl rand -base64 32 | tr -d '=\n' | tr '+/' '-_')"
key_hash="$(printf '%s' "$raw_key" | openssl dgst -sha256 -r | awk '{print $1}')"
expires_at="$(python3 -c "import time; print(int((time.time() + int('$valid_days') * 86400) * 1000))")"

updated_json="$(printf '%s' "$registry_json" | jq -c \
  --arg org "$organization" --arg team "$team" --arg env "$environment" \
  --arg hash "$key_hash" --argjson expires "$expires_at" \
  '[.[] | if has("value") then .value[] else . end]
   | map(select(.orgSlug != $org or .teamId != $team or (.env // null) != $env))
   + [{keyHash:$hash, orgSlug:$org, teamId:$team, expiresAt:$expires, revoked:false, env:$env}]')"
updated_base64="$(printf '%s' "$updated_json" | base64 | tr -d '\n')"
patch="$(jq -nc --arg value "$updated_base64" '{data:{INGESTION_KEYS_JSON:$value}}')"

kubectl --context "$kube_context" -n "$namespace" patch secret telemetry-ingestion --type merge -p "$patch" >/dev/null

temp_env="$(mktemp)"
trap 'rm -f -- "$temp_env"' EXIT
grep -Ev '^(OTEL_EXPORTER_OTLP_ENDPOINT|OTEL_EXPORTER_OTLP_HEADERS|OTEL_EXPORTER_OTLP_PROTOCOL|OTEL_SERVICE_NAME)=' "$env_path" >"$temp_env" || true
cat >>"$temp_env" <<EOF

OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:3001/api/ingest
OTEL_EXPORTER_OTLP_HEADERS=x-api-key=$raw_key
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_SERVICE_NAME=product-hub-api
EOF
mv -- "$temp_env" "$env_path"
trap - EXIT

kubectl --context "$kube_context" -n "$namespace" rollout restart deployment/backend >/dev/null
kubectl --context "$kube_context" -n "$namespace" rollout status deployment/backend --timeout=120s

echo "Telemetry-hub ingestion key registered and ProductHub .env configured."
echo "Scope: $organization / $team / $environment"
echo "The raw key was written only to ProductHub .env and was not printed."
