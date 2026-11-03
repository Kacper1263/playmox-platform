#!/bin/sh
set -eu

APPSETTINGS_PATH="/usr/share/nginx/html/appsettings.json"
API_BASE_URL="${API_BASE_URL:-http://localhost:8080/}"

if [ ! -f "$APPSETTINGS_PATH" ]; then
  exit 0
fi

escaped_api_base_url="$(printf '%s' "$API_BASE_URL" | sed -e 's/[\/&]/\\&/g')"
sed -i "s/__API_BASE_URL__/${escaped_api_base_url}/g" "$APPSETTINGS_PATH"
