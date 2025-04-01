### About This Sample API
> This sample API is designed to assist SIPS.Connect Consumers by demonstrating a live integration with the SIPS.Connect platform. It serves as a practical reference for understanding how to interact with the platform’s endpoints, including authentication, message submission, and response handling.

The primary goal of this sample is to help developers and integrators:

* Understand the expected API request and response formats

* Simulate real-world interactions with the SIPS.Connect system

* Validate their integration logic before moving to production

---

### Running the Application Locally

After cloning this repository to your local machine, navigate to the root of the project directory and execute the following command to build the Docker image:

```bash
docker build -t sips.connect.consumer .
```

Once the build process is complete, run the container with the following command:

```bash
docker run --rm -p 8091:8080 sips.connect.consumer
```

After the container is up and running, open your browser and go to:

```
http://localhost:8091/swagger/index.html
```

to access the Swagger UI and begin exploring the API.

---

## Disclaimer

> **Note:** This repository contains an example API consumer intended for educational and reference purposes only.  
> It is **not production-ready** and should not be used in live environments without proper review, security hardening, and performance optimization.

---
