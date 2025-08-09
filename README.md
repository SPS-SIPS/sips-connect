# SIPS Connect Platform Easy Deployment Guide.

This guide provides a straightforward approach to deploying the SIPS Connect Platform using Docker and Docker Compose. It is designed for users who may not be familiar with Docker or containerization.


## Prerequisites

1. **Docker**: Ensure Docker is installed on your system. You can download it from [Docker's official website](https://www.docker.com/get-started).
2. **Docker Compose**: This is typically included with Docker Desktop installations. If you're using Linux, you may need to install it separately. Follow the instructions on [Docker Compose's official documentation](https://docs.docker.com/compose/install/).
3. **Git**: Ensure Git is installed to clone the repository. You can download it from [Git's official website](https://git-scm.com/downloads).
4. **OpenSSL**: Required for generating self-signed certificates for HTTPS. Most Linux and macOS systems have it pre-installed. On Windows, you can get it from [OpenSSL for Windows](https://slproweb.com/products/Win32OpenSSL.html).
5. **Environment Variables**: You will need to set up environment variables for your deployment. Create a `.env` file in the root directory of the project.
6. **PKI Certificate from SPS**: You must request a PKI certificate from SPS, as it is always required to sign transactions. The self-signed certificate instructions below are for application deployment.

## Important Environment Variables

| Variable                        | Description                                                      |
|---------------------------------|------------------------------------------------------------------|
| DB_PASSWORD                     | Database password used by Postgres, Keycloak, and SIPS Connect   |
| KEYCLOAK_KEYSTORE_PASSWORD      | Password for Keycloak's PKCS#12 SSL certificate (keycloak.p12)    |
| SIPS_CONNECT_TLS_PFX_PASSWORD   | Password for SIPS Connect's PKCS#12 SSL certificate (sips-connect.pfx) |

**Keep these passwords secure and do not share them publicly.**

## Application Ports

You can configure the application ports by changing the corresponding variables in your `.env` file. This allows you to avoid conflicts or fit your environment.

| Service         | Variable Name           | Default Host:Container | Description                |
|-----------------|------------------------|------------------------|----------------------------|
| SIPS Connect    | SIPS_CONNECT_PORT, SIPS_CONNECT_HTTPS_PORT | 9080:8080, 9443:443 | Main API service (HTTP/HTTPS) |
| Keycloak (IDP)  | IDP_PORT               | 8443:8443              | Identity provider (OIDC, HTTPS) |
| Grafana         | GRAFANA_PORT           | 9083:3000              | Monitoring dashboard       |
| Loki            | LOKI_PORT              | 3500:3100              | Log aggregation            |
| Corebank        | CB_PORT                | 9082:8080              | Example consumer service   |

Change these variables in your `.env` file to suit your needs.
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
3. **Configure Environment Variables**: Copy the provided `.env.example` file to `.env` in the root directory of the project:

   ```bash
   cp .env.example .env
   ```

   Then edit the `.env` file and update the variables for your environment. The most important variables are explained at the top of this README.
4. **Start the Services**: Use Docker Compose to start the services defined in the `docker-compose.yml` file:
   ```bash
   docker-compose up -d or docker compose up -d 
   ```
5. **Access the Application**: Once the services are up and running, access the applications using your host machine's IP address and the ports you configured in your `.env` file. For example:

    - **SIPS Connect API (Swagger):**  
       `http://<HOST_MACHINE_IP>:<SIPS_CONNECT_PORT>/swagger/index.html`
    - **SIPS Connect API (HTTPS):**  
       `https://<HOST_MACHINE_IP>:<SIPS_CONNECT_HTTPS_PORT>/swagger/index.html`
    - **Keycloak Admin Console:**  
       `https://<HOST_MACHINE_IP>:<IDP_PORT>/`
    - **Grafana Dashboard:**  
       `http://<HOST_MACHINE_IP>:<GRAFANA_PORT>/`

    Replace `<HOST_MACHINE_IP>` and the port variables with the values you set in your `.env` file.
