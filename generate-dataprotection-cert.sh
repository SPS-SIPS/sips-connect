#!/bin/bash

# Generate Data Protection Certificate for SIPS.Connect
# This certificate is used to encrypt ASP.NET Core Data Protection keys at rest

set -e

echo "🔐 Generating Data Protection Certificate for SIPS.Connect"
echo "============================================================"
echo ""

# Check if openssl is installed
if ! command -v openssl &> /dev/null; then
    echo "❌ Error: openssl is not installed"
    echo "Install it with: apt-get install openssl (Ubuntu) or brew install openssl (macOS)"
    exit 1
fi

# Create certs directory if it doesn't exist
mkdir -p ./certs

# Check if certificate already exists
if [ -f "./certs/dataprotection.pfx" ]; then
    echo "⚠️  Certificate already exists at ./certs/dataprotection.pfx"
    read -p "Do you want to overwrite it? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 0
    fi
fi

# Prompt for password
echo ""
echo "Enter a strong password for the certificate (20+ characters recommended):"
read -s CERT_PASSWORD
echo ""
echo "Confirm password:"
read -s CERT_PASSWORD_CONFIRM
echo ""

if [ "$CERT_PASSWORD" != "$CERT_PASSWORD_CONFIRM" ]; then
    echo "❌ Passwords don't match"
    exit 1
fi

if [ ${#CERT_PASSWORD} -lt 12 ]; then
    echo "⚠️  Warning: Password is shorter than 12 characters"
    read -p "Continue anyway? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        exit 0
    fi
fi

echo ""
echo "📝 Generating certificate..."
echo ""

# Generate private key and certificate
openssl req -x509 -newkey rsa:4096 \
  -keyout ./certs/dataprotection-key.pem \
  -out ./certs/dataprotection-cert.pem \
  -days 3650 -nodes \
  -subj "/CN=SIPS.Connect.DataProtection/O=SIPS/C=SO" \
  2>/dev/null

# Convert to PFX format
openssl pkcs12 -export \
  -out ./certs/dataprotection.pfx \
  -inkey ./certs/dataprotection-key.pem \
  -in ./certs/dataprotection-cert.pem \
  -password pass:$CERT_PASSWORD \
  2>/dev/null

# Clean up PEM files (keep only PFX)
rm ./certs/dataprotection-key.pem ./certs/dataprotection-cert.pem

# Set proper permissions
chmod 600 ./certs/dataprotection.pfx

echo "✅ Certificate generated successfully!"
echo ""
echo "📁 Certificate location: ./certs/dataprotection.pfx"
echo "🔒 Permissions: 600 (read/write for owner only)"
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "📋 Next Steps:"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "1. Encrypt the certificate password:"
echo "   dotnet run -- secrets encrypt \"YOUR_PASSWORD\""
echo ""
echo "2. Add to appsettings.json:"
echo "   {"
echo "     \"DataProtection\": {"
echo "       \"CertificatePath\": \"./certs/dataprotection.pfx\","
echo "       \"CertificatePassword\": \"ENCRYPTED:CfDJ8...\""
echo "     }"
echo "   }"
echo ""
echo "3. Delete old unencrypted keys:"
echo "   rm -rf keys/*"
echo ""
echo "4. Restart application to generate new encrypted keys:"
echo "   dotnet run"
echo ""
echo "5. Verify in logs:"
echo "   Look for: 'Data Protection keys encrypted with certificate'"
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "⚠️  IMPORTANT: Backup this certificate securely!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Without this certificate, you cannot decrypt your Data Protection keys!"
echo "Store it in a secure location with the password."
echo ""
