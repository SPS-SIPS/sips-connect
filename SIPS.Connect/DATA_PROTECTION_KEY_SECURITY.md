# Data Protection Key Security Guide

## 🔐 Problem: Unencrypted Key Files

ASP.NET Core Data Protection keys are stored as **plain XML files** by default in the `keys/` directory. These files contain the master encryption keys used to encrypt/decrypt your application secrets.

**Example key file:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<key id="6236f9b4-6964-4c67-b91e-061e86660530" version="1">
  <creationDate>2025-11-18T11:00:00Z</creationDate>
  <activationDate>2025-11-18T11:00:00Z</activationDate>
  <expirationDate>2026-02-16T11:00:00Z</expirationDate>
  <descriptor deserializerType="Microsoft.AspNetCore.DataProtection...">
    <descriptor>
      <encryption algorithm="AES_256_CBC" />
      <validation algorithm="HMACSHA256" />
      <masterKey>
        <!-- UNENCRYPTED KEY DATA HERE -->
        <value>BASE64_ENCODED_KEY_MATERIAL</value>
      </masterKey>
    </descriptor>
  </descriptor>
</key>
```

⚠️ **Security Risk**: Anyone with access to these files can decrypt ALL your application secrets!

---

## ✅ Solution: Encrypt Keys at Rest

The application now supports **three methods** to encrypt Data Protection keys:

### **Method 1: X509 Certificate (Recommended for Production)**
### **Method 2: Windows DPAPI (Windows servers only)**
### **Method 3: File System Permissions (Development/Linux)**

---

## 🎯 Method 1: X509 Certificate Encryption (Recommended)

### **How it works:**
- Keys are encrypted using a certificate's public key
- Only the certificate's private key can decrypt them
- Works on all platforms (Windows, Linux, macOS, Docker)

### **Step 1: Generate a Certificate**

```bash
# Generate a self-signed certificate for key encryption
openssl req -x509 -newkey rsa:4096 \
  -keyout dataprotection-key.pem \
  -out dataprotection-cert.pem \
  -days 3650 -nodes \
  -subj "/CN=SIPS.Connect.DataProtection"

# Convert to PFX format (required by .NET)
openssl pkcs12 -export \
  -out dataprotection.pfx \
  -inkey dataprotection-key.pem \
  -in dataprotection-cert.pem \
  -password pass:YourStrongPassword123

# Secure the files
chmod 600 dataprotection.pfx
```

### **Step 2: Configure in appsettings.json**

```json
{
  "DataProtection": {
    "CertificatePath": "./certs/dataprotection.pfx",
    "CertificatePassword": "ENCRYPTED:CfDJ8..."
  }
}
```

**Important**: Encrypt the certificate password using the secret management tool:
```bash
dotnet run -- secrets encrypt "YourStrongPassword123"
# Copy output to CertificatePassword
```

### **Step 3: Deploy Certificate**

```bash
# Copy certificate to deployment server
scp dataprotection.pfx user@server:/path/to/deployment/certs/

# Update docker-compose.yml (already has certs volume)
# volumes:
#   - ./certs:/certs:ro

# Set proper permissions
chmod 600 /path/to/deployment/certs/dataprotection.pfx
```

### **Step 4: Restart Application**

```bash
# Delete old unencrypted keys
rm -rf keys/*

# Restart to generate new encrypted keys
docker compose restart sips-connect

# Check logs
docker compose logs sips-connect | grep "Data Protection"
# Should show: "Data Protection keys encrypted with certificate"
```

### **Verification**

```bash
# Check new key files - should show encrypted content
cat keys/key-*.xml
```

**Encrypted key file example:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<key id="..." version="1">
  <creationDate>2025-11-18T11:00:00Z</creationDate>
  <descriptor>
    <encryptedSecret decryptorType="Microsoft.AspNetCore.DataProtection.XmlEncryption.EncryptedXmlDecryptor">
      <EncryptedData Type="http://www.w3.org/2001/04/xmlenc#Element">
        <!-- ENCRYPTED KEY DATA - SAFE! -->
        <CipherData>
          <CipherValue>BASE64_ENCRYPTED_DATA</CipherValue>
        </CipherData>
      </EncryptedData>
    </encryptedSecret>
  </descriptor>
</key>
```

✅ Keys are now encrypted!

---

## 🪟 Method 2: Windows DPAPI (Windows Only)

### **How it works:**
- Uses Windows Data Protection API
- Keys encrypted using machine or user credentials
- Automatic on Windows servers
- **Not portable** - keys only work on the same machine

### **Configuration:**

No configuration needed! The application automatically uses DPAPI on Windows if no certificate is configured.

### **Verification:**

```powershell
# Start application
dotnet run

# Check logs
# Should show: "Data Protection keys encrypted with Windows DPAPI"
```

### **Limitations:**
- ❌ Keys not portable between servers
- ❌ Doesn't work in Docker Linux containers
- ❌ Requires Windows Server

---

## 🐧 Method 3: File System Permissions (Development/Linux)

### **How it works:**
- Keys stored unencrypted
- Protected by file system permissions
- **Not recommended for production**

### **Configuration:**

```bash
# Restrict access to keys directory
chmod 700 keys/
chmod 600 keys/*.xml

# Ensure only app user can access
chown -R appuser:appuser keys/
```

### **Verification:**

```bash
# Check permissions
ls -la keys/
# Should show: drwx------ (700)

# Check file permissions
ls -la keys/*.xml
# Should show: -rw------- (600)
```

### **Limitations:**
- ❌ Keys still unencrypted
- ❌ Root user can still read them
- ❌ Not suitable for production

---

## 📊 Comparison

| Method | Security | Portability | Platform | Recommended |
|--------|----------|-------------|----------|-------------|
| **X509 Certificate** | ⭐⭐⭐⭐⭐ | ✅ Yes | All | ✅ Production |
| **Windows DPAPI** | ⭐⭐⭐⭐ | ❌ No | Windows | Windows only |
| **File Permissions** | ⭐⭐ | ✅ Yes | Linux/macOS | Development |

---

## 🚀 Production Deployment Checklist

### **Before Deployment:**

- [ ] Generate Data Protection certificate
- [ ] Encrypt certificate password
- [ ] Add certificate configuration to appsettings.json
- [ ] Copy certificate to deployment server
- [ ] Set certificate file permissions (600)
- [ ] Delete old unencrypted keys

### **After Deployment:**

- [ ] Verify logs show "Data Protection keys encrypted with certificate"
- [ ] Check key files contain `<encryptedSecret>` elements
- [ ] Test application can decrypt secrets
- [ ] Backup encrypted certificate securely
- [ ] Document certificate location and password

---

## 🔄 Key Rotation

### **When to Rotate:**
- Every 90 days (recommended)
- After security incident
- When certificate expires
- When moving to new infrastructure

### **How to Rotate:**

```bash
# 1. Generate new certificate
openssl pkcs12 -export \
  -out dataprotection-new.pfx \
  -inkey dataprotection-key.pem \
  -in dataprotection-cert.pem \
  -password pass:NewPassword456

# 2. Update appsettings.json with new certificate
# DataProtection:CertificatePath = "./certs/dataprotection-new.pfx"
# DataProtection:CertificatePassword = "ENCRYPTED:..."

# 3. Backup old keys
tar -czf keys-backup-$(date +%Y%m%d).tar.gz keys/

# 4. Delete old keys
rm -rf keys/*

# 5. Restart application (generates new encrypted keys)
docker compose restart sips-connect

# 6. Re-encrypt all application secrets with new keys
dotnet run -- secrets encrypt-file appsettings.json
```

---

## 🆘 Troubleshooting

### **Problem: "Failed to load Data Protection certificate"**

**Cause:** Certificate file not found or wrong password

**Solution:**
```bash
# Check certificate exists
ls -la certs/dataprotection.pfx

# Test certificate can be loaded
openssl pkcs12 -in certs/dataprotection.pfx -noout
# Enter password when prompted

# Verify password is correct in appsettings.json
```

### **Problem: Keys still unencrypted after configuration**

**Cause:** Old keys generated before certificate was configured

**Solution:**
```bash
# Delete old keys
rm -rf keys/*

# Restart to generate new encrypted keys
docker compose restart sips-connect
```

### **Problem: "Cannot decrypt secrets" after key rotation**

**Cause:** Application secrets encrypted with old keys

**Solution:**
```bash
# Restore old keys temporarily
tar -xzf keys-backup-YYYYMMDD.tar.gz

# Decrypt secrets
dotnet run -- secrets decrypt-file appsettings.json > appsettings-plain.json

# Switch to new keys
rm -rf keys/*
docker compose restart sips-connect

# Re-encrypt with new keys
dotnet run -- secrets encrypt-file appsettings-plain.json
mv appsettings-plain.json appsettings.json
```

---

## 🛡️ Security Best Practices

### **Certificate Management:**

1. **Store certificate securely**
   - Use hardware security module (HSM) if available
   - Or encrypted storage with restricted access
   - Never commit to Git

2. **Protect certificate password**
   - Use strong password (20+ characters)
   - Store encrypted in appsettings.json
   - Or use environment variable

3. **Backup certificate**
   - Keep encrypted backup in secure location
   - Document recovery procedure
   - Test restore process

### **Key File Protection:**

1. **Restrict file access**
   ```bash
   chmod 700 keys/
   chmod 600 keys/*.xml
   chown appuser:appuser keys/
   ```

2. **Monitor access**
   ```bash
   # Enable audit logging
   auditctl -w /path/to/keys -p rwa -k dataprotection_keys
   ```

3. **Regular backups**
   ```bash
   # Automated backup script
   tar -czf keys-backup-$(date +%Y%m%d).tar.gz keys/
   gpg -c keys-backup-$(date +%Y%m%d).tar.gz
   ```

---

## 📝 Configuration Examples

### **Development (Unencrypted - Warning shown)**

```json
{
  "DataProtection": {
    // No configuration - keys stored unencrypted
  }
}
```

**Log output:**
```
⚠️  Data Protection keys are stored UNENCRYPTED. For production, configure DataProtection:CertificatePath
```

### **Production (Certificate-encrypted)**

```json
{
  "DataProtection": {
    "CertificatePath": "./certs/dataprotection.pfx",
    "CertificatePassword": "ENCRYPTED:CfDJ8LT5NmJkaWdMuR4GHoZmBTBGFIayVVur3z1m62ZTzpTI..."
  }
}
```

**Log output:**
```
Data Protection keys encrypted with certificate: ./certs/dataprotection.pfx
```

---

## 🎯 Summary

### **Current Implementation:**

✅ **Automatic encryption detection**
- Certificate (if configured) → Best security
- Windows DPAPI (if on Windows) → Good security
- Unencrypted (with warning) → Development only

✅ **Cross-platform support**
- Works on Windows, Linux, macOS, Docker

✅ **Flexible configuration**
- Certificate path in appsettings.json
- Password encrypted using secret management

### **Recommended Setup:**

**Development:**
```bash
# No configuration needed
# Warning shown in logs
```

**Production:**
```bash
# Generate certificate
openssl pkcs12 -export -out dataprotection.pfx ...

# Configure in appsettings.json
{
  "DataProtection": {
    "CertificatePath": "./certs/dataprotection.pfx",
    "CertificatePassword": "ENCRYPTED:..."
  }
}

# Deploy and verify
docker compose up -d
docker compose logs | grep "Data Protection"
```

**Your Data Protection keys are now secure!** 🔐✅
