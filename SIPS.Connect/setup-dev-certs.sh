#!/bin/bash
set -e

echo "🔐 Restoring SIPS Connect Development Certificates"
echo "=================================================="

# Create certs directory
mkdir -p ./certs

# 1. Generate Kestrel SSL Certificate (PFX)
echo "📝 Generating sips-connect.pfx..."
openssl req -x509 -newkey rsa:4096 \
  -keyout ./certs/sips-connect-key.pem \
  -out ./certs/sips-connect-cert.pem \
  -days 365 -nodes \
  -subj "/CN=localhost/O=SIPS.Dev/C=SO" \
  2>/dev/null

openssl pkcs12 -export \
  -out ./certs/sips-connect.pfx \
  -inkey ./certs/sips-connect-key.pem \
  -in ./certs/sips-connect-cert.pem \
  -password pass:YOUR_PASSWORD \
  2>/dev/null

# 2. Generate XADES Signing Certificates (PEM)
echo "📝 Generating XADES certificates (certificate.pem, private.key)..."
openssl req -x509 -newkey rsa:2048 \
  -keyout ./certs/private.key \
  -out ./certs/certificate.pem \
  -days 365 -nodes \
  -subj "/CN=ZKBASOS0/O=SIPS.Audit/C=SO" \
  2>/dev/null

cp ./certs/certificate.pem ./certs/chain.pem

# 3. Generate Data Protection Certificate (PFX)
echo "📝 Generating dataprotection.pfx..."
openssl req -x509 -newkey rsa:4096 \
  -keyout ./certs/dp-key.pem \
  -out ./certs/dp-cert.pem \
  -days 3650 -nodes \
  -subj "/CN=SIPS.Connect.DataProtection/O=SIPS/C=SO" \
  2>/dev/null

openssl pkcs12 -export \
  -out ./certs/dataprotection.pfx \
  -inkey ./certs/dp-key.pem \
  -in ./certs/dp-cert.pem \
  -password pass:YOUR_PASSWORD \
  2>/dev/null

# Cleanup temporary PEM files
rm ./certs/sips-connect-key.pem ./certs/sips-connect-cert.pem
rm ./certs/dp-key.pem ./certs/dp-cert.pem

echo "✅ Certificates restored successfully!"
ls -l ./certs
