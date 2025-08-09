# SIPS Connect Platform Easy Deployment Guide.

This guide provides a straightforward approach to deploying the SIPS Connect Platform using Docker and Docker Compose. It is designed for users who may not be familiar with Docker or containerization.


## Prerequisites

1. **Docker**: Ensure Docker is installed on your system. You can download it from [Docker's official website](https://www.docker.com/get-started).
2. **Docker Compose**: This is typically included with Docker Desktop installations. If you're using Linux, you may need to install it separately. Follow the instructions on [Docker Compose's official documentation](https://docs.docker.com/compose/install/).
3. **Git**: Ensure Git is installed to clone the repository. You can download it from [Git's official website](https://git-scm.com/downloads).
4. **OpenSSL**: Required for generating self-signed certificates for HTTPS. Most Linux and macOS systems have it pre-installed. On Windows, you can get it from [OpenSSL for Windows](https://slproweb.com/products/Win32OpenSSL.html).
5. **Environment Variables**: You will need to set up environment variables for your deployment. Create a `.env` file in the root directory of the project.
6. **PKI Certificate from SPS**: You must request a PKI certificate from SPS, as it is always required to sign transactions. The self-signed certificate instructions below are for application deployment.

## Sample `.env` File

```env
Serilog__WriteTo__File__Args__path=/logs/log.log
Serilog__MinimumLevel__Override__Microsoft=Information
Serilog__MinimumLevel__Override__System=Information
Serilog__MinimumLevel__Default=Information


ASPNETCORE_ENVIRONMENT=Development

PGUSER=postgres
POSTGRES_PASSWORD=<YOUR_STRONG_DB_PASSWORD>
POSTGRES_DB=postgres

ConnectionStrings__db="Host=sips-connect-db;Database=sips.db.agro;Include Error Detail=True;Username=postgres;Password=<YOUR_STRONG_DB_PASSWORD>;"
KC_DB=postgres
KC_DB_USERNAME=postgres
KC_DB_PASSWORD=<YOUR_STRONG_DB_PASSWORD>
KC_DB_URL="jdbc:postgresql://sips-connect-db/postgres"
KEYCLOAK_HOST_IP=<YOUR_KEYCLOAK_HOST_IP>
KEYCLOAK_KEYSTORE_PASSWORD=<YOUR_KEYCLOAK_KEYSTORE_PASSWORD>

Keycloak__Realm__Host="idp:8443"
Keycloak__Realm__Protocol="https"
Keycloak__Realm__ValidateIssuer=true
Keycloak__Realm__Name="mgt"
Keycloak__Realm__Audience="sc-api"
Keycloak__Realm__ValidIssuers__0="https://idp:8443"
Keycloak__Realm__ValidIssuers__1="https://<YOUR_KEYCLOAK_HOST_IP>:8443"

# Compose Ports::
SIPS_CONNECT_PORT=9080
IDP_PORT=8443
LOKI_PORT=3500
GRAFANA_PORT=9083
INSTITUTION_NAME=<YOUR_INSTITUTION_NAME>
DB_HOST=sips-connect-db
DB_PORT=5432
CB_PORT=9082

# DB File - This is the file where dummy accounts/transactions are stored
DbPersistenceFile="/app/db.json"

# Kestrel HTTPS certificate override for SIPS Connect
Kestrel__Endpoints__Https__Certificate__Path=/certs/sips-connect.pfx
Kestrel__Endpoints__Https__Certificate__Password=YOUR_PASSWORD
Kestrel__Endpoints__Https__Url=https://0.0.0.0:443
```


## Generating Self-Signed Certificates for HTTPS

To enable HTTPS for Keycloak and the Next.js portal, generate a self-signed certificate and key as follows (replace the IP with your own):

```sh
# 1. Generate a private key
openssl genrsa -out tls.key 2048

# 2. Generate a self-signed certificate (replace 172.16.31.20 with your server's IP or DNS name)
openssl req -new -x509 -key tls.key -out tls.crt -days 365 -subj "/CN=172.16.31.20"

# 3. Convert the key and certificate to a PKCS#12 file (for Keycloak, set your own password)
openssl pkcs12 -export -in tls.crt -inkey tls.key -out keycloak.p12 -name keycloak -password pass:YOUR_PASSWORD
# 4. Convert the key and certificate to a PFX file (for SIPS Connect, set your own password)
openssl pkcs12 -export -in tls.crt -inkey tls.key -out sips-connect.pfx -name sips-connect -password pass:YOUR_PASSWORD
```

Place `tls.crt` and `tls.key` in the appropriate `certs/` directory for your Next.js app, and `keycloak.p12` in the `certs/` directory for Keycloak as referenced in your `docker-compose.yml` files.

---
## Deployment Steps

1. **Clone the Repository**: Open your terminal and run the following command to clone the SIPS Connect Platform repository:

   ```bash
   git clone https://github.com/SPS-SIPS/Connect.Deployment.git
   ```

2. **Navigate to the Project Directory**: Change into the project directory:
   ```bash
   cd Connect.Deployment
   ```
3. **Create the `.env` File**: Create a `.env` file in the root directory of the project and populate it with your environment variables as shown above.
4. **Start the Services**: Use Docker Compose to start the services defined in the `docker-compose.yml` file:
   ```bash
   docker-compose up -d or docker compose up -d 
   ```
5. **Access the Application**: Once the services are up and running, you can access the SIPS Connect Platform at `http://localhost:9080/swagger/index.html` in your web browser.
6. **Access Keycloak**: You can access Keycloak at `http://localhost:9081/auth` to manage your authentication settings.
7. **Access Grafana**: Grafana can be accessed at `http://localhost:9083` for monitoring and visualization.
