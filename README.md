# SIPS Connect Platform Easy Deployment Guide.

This guide provides a straightforward approach to deploying the SIPS Connect Platform using Docker and Docker Compose. It is designed for users who may not be familiar with Docker or containerization.

## Prerequisites

1. **Docker**: Ensure Docker is installed on your system. You can download it from [Docker's official website](https://www.docker.com/get-started).
2. **Docker Compose**: This is typically included with Docker Desktop installations. If you're using Linux, you may need to install it separately. Follow the instructions on [Docker Compose's official documentation](https://docs.docker.com/compose/install/).
3. **Git**: Ensure Git is installed to clone the repository. You can download it from [Git's official website](https://git-scm.com/downloads).
4. **Environment Variables**: You will need to set up environment variables for your deployment. Create a `.env` file in the root directory of the project.

## Sample `.env` File

```env
Serilog__WriteTo__File__Args__path=/logs/log.log
Serilog__MinimumLevel__Override__Microsoft=Information
Serilog__MinimumLevel__Override__System=Information
Serilog__MinimumLevel__Default=Information

ASPNETCORE_ENVIRONMENT=Development

PGUSER=postgres
POSTGRES_PASSWORD=<YOUR_DB_PASSWORD>
POSTGRES_DB=postgres

ConnectionStrings__db="Host=sips-connect-db;Database=sips.db.agro;Include Error Detail=True;Username=postgres;Password=<YOUR_DB_PASSWORD>;"
KC_DB=postgres
KC_DB_USERNAME=postgres
KC_DB_PASSWORD=<YOUR_DB_PASSWORD>
KC_DB_URL="jdbc:postgresql://sips-connect-db/postgres"

Keycloak__Realm__Host="idp:8080"
Keycloak__Realm__Protocol="http"
Keycloak__Realm__ValidateIssuer=false
Keycloak__Realm__Name="mgt"
Keycloak__Realm__Audience="sc-api"
Keycloak__Realm__ValidIssuers__0="http://idp:8080"
Keycloak__Realm__ValidIssuers__1="http://<YOUR_IDP_HOST_IP_Address>:9081"

# Compose Ports:
SIPS_CONNECT_PORT=9080
IDP_PORT=9081
LOKI_PORT=3500
GRAFANA_PORT=9083
INSTITUTION_NAME=<YOUR_INSTITUTION_NAME> e.g. "Agro"
DB_HOST=sips-connect-db
DB_PORT=5432
CB_PORT=9082
```

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
