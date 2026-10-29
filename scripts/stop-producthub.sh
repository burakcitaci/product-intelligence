#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/.." && pwd)"
state_file="$project_root/.run/producthub-processes.json"

if [[ -f "$state_file" ]]; then
  frontend_pid="$(node -e "const fs=require('fs'); const s=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); process.stdout.write(String(s.FrontendPid || ''));" "$state_file")"
  if [[ "$frontend_pid" =~ ^[0-9]+$ ]]; then
    pkill -TERM -P "$frontend_pid" 2>/dev/null || true
    kill -TERM "$frontend_pid" 2>/dev/null || true
  fi
  rm -f -- "$state_file" "$project_root/.run/frontend.pid"
fi

cd "$project_root"
docker compose stop api >/dev/null

echo "Backend and frontend stopped."
echo "SQL Server and Qdrant are still running. Use 'docker compose down' to stop them too."
