#!/usr/bin/env bash
set -euo pipefail

product_id="${1:-}"
language="${2:-de}"
api_base_url="${API_BASE_URL:-http://localhost:5099}"

if [[ -z "$product_id" ]]; then
  echo "Usage: $0 <product-id> [de|en]" >&2
  exit 1
fi
if [[ "$language" != "de" && "$language" != "en" ]]; then
  echo "Language must be 'de' or 'en'." >&2
  exit 1
fi

idempotency_key="manual-$(date +%s)-$RANDOM"
curl --fail-with-body --silent --show-error \
  --request POST \
  --header "Content-Type: application/json" \
  --header "Idempotency-Key: $idempotency_key" \
  --data "{\"language\":\"$language\"}" \
  "$api_base_url/api/products/$product_id/drafts/generate"
echo
