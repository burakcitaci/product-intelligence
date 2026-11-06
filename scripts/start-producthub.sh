#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/.." && pwd)"
web_root="$project_root/src/ProductHub.Web"
run_root="$project_root/.run"
state_file="$run_root/producthub-processes.json"
skip_build=false

if [[ "${1:-}" == "--skip-build" ]]; then
  skip_build=true
fi

if [[ ! -f "$project_root/.env" ]]; then
  echo "Missing .env. Copy .env.example to .env and set MSSQL_SA_PASSWORD." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source "$project_root/.env"
set +a

if [[ -z "${MSSQL_SA_PASSWORD:-}" ]]; then
  echo "MSSQL_SA_PASSWORD is missing from .env." >&2
  exit 1
fi

for command_name in docker node npm; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "Required command '$command_name' was not found." >&2
    exit 1
  }
done

cd "$project_root"
echo "Building and starting API, SQL Server, and Qdrant..."
if $skip_build; then
  docker compose up -d --wait
else
  docker compose up -d --build --wait
fi

if [[ ! -d "$web_root/node_modules" ]]; then
  echo "Installing frontend packages..."
  (cd "$web_root" && npm install)
fi

mkdir -p "$run_root"
echo "Starting frontend..."
pushd "$web_root" >/dev/null
nohup npm run dev -- --host 127.0.0.1 >"$run_root/frontend.log" 2>"$run_root/frontend-error.log" &
frontend_pid=$!
popd >/dev/null
sleep 3
if ! kill -0 "$frontend_pid" 2>/dev/null; then
  echo "Frontend stopped during startup. See $run_root/frontend-error.log" >&2
  exit 1
fi

printf '{"FrontendPid":%s,"StartedAt":"%s"}\n' "$frontend_pid" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >"$state_file"

echo
echo "Product Hub is running."
echo "Frontend: http://localhost:5173"
echo "Backend:  http://localhost:5099"
echo "Frontend logs: $run_root"
echo "Backend logs:  docker compose logs -f api"
echo "Stop with: ./scripts/stop-producthub.sh"
