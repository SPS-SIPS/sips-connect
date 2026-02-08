# SPS Dashboard UI - Local Development Guide

This guide will help you set up and run the SPS Dashboard UI locally with HTTPS enabled using a self-signed SSL certificate.

## Prerequisites

1. **Node.js** (v18+ recommended)
2. **npm** (comes with Node.js)
3. **Docker & Docker Compose** (optional, for containerized development)
4. **OpenSSL** (for generating self-signed certificates)

## Directory Structure

```
sps-dashboard-ui/
├── certs/
│   ├── tls.crt         # SSL certificate (to be generated)
│   └── tls.key         # SSL private key (to be generated)
├── components/
├── pages/
├── public/
├── server-https.js
├── entrypoint-https.sh
├── Dockerfile
├── docker-compose.yml
├── package.json
├── .env.local
└── ...
```

## Generating a Self-Signed SSL Certificate

Run these commands inside the `sps-dashboard-ui` directory:

```sh
mkdir -p certs
cd certs

# 1. Generate a private key
openssl genrsa -out tls.key 2048

# 2. Generate a self-signed certificate (replace 127.0.0.1 with your local IP or DNS if needed)
openssl req -new -x509 -key tls.key -out tls.crt -days 365 -subj "/CN=127.0.0.1"
```

This will create `tls.crt` and `tls.key` in the `certs/` directory.

## Running the App with Docker Compose

1. Build and start the app:
   ```sh
   docker-compose up --build
   ```
2. Visit [https://localhost:3000](https://localhost:3000) in your browser. Accept the self-signed certificate warning.

## Running the App Locally (without Docker)

1. Install dependencies:
   ```sh
   npm install
   ```
2. Make sure the certs are in `certs/tls.crt` and `certs/tls.key`.
3. Start the server:
   ```sh
   npm run build
   node server-https.js
   ```
4. Visit [https://localhost:3000](https://localhost:3000).

---

**Note:** For production, you must use a valid certificate from a trusted Certificate Authority (CA) or a PKI certificate provided by SPS.
# SIPS Dashboard UI

This project provides a dashboard interface for interacting with the **SIPS Connect** platform. The dashboard allows users to manage and utilize SIPS Connect features through a user-friendly web interface.

## Prerequisites

- [Docker](https://www.docker.com/products/docker-desktop) installed on your machine.
- [Docker Compose](https://docs.docker.com/compose/) installed.

## Setup Instructions

### 1. Create the Docker Network

This project requires a Docker network named `sips-network`. If it does not already exist, create it with the following command:

```bash
docker network create --driver bridge sips-network
```

### 2. Configure Environment Variables

Copy the provided `.env.local` file or create your own in the project root. Update the variables as needed for your environment:

```env
NEXT_PUBLIC_API_URL=http://localhost:8081
NEXT_PUBLIC_KEYCLOAK_URL=https://localhost:8080
NEXT_PUBLIC_KEYCLOAK_REALM=your_realm
NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=your_client_id
```

- **NEXT_PUBLIC_API_URL**: The base URL for the SIPS Connect API.
- **NEXT_PUBLIC_KEYCLOAK_URL**, **REALM**, **CLIENT_ID**: Keycloak authentication settings.

### 3. Build and Run the Dashboard

Start the dashboard using Docker Compose:

```bash
docker-compose up --build
```

The dashboard will be available at [http://localhost:3000](http://localhost:3000).

## Why This Dashboard?

This dashboard was created to provide a convenient and efficient way to interact with the **SIPS Connect** platform. It enables users to manage platform features, monitor activity, and perform administrative tasks through a centralized web interface.

---
