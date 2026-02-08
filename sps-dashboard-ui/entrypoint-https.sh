#!/bin/sh
# Entrypoint script to start Next.js with HTTPS using self-signed certs

CERT_PATH=/app/certs/tls.crt
KEY_PATH=/app/certs/tls.key

if [ ! -f "$CERT_PATH" ] || [ ! -f "$KEY_PATH" ]; then
  echo "SSL certificate or key not found!"
  exit 1
fi

exec node server-https.js
