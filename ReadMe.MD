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
NEXT_PUBLIC_KEYCLOAK_URL=http://localhost:8080
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
