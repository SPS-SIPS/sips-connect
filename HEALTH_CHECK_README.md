# Health Check Endpoint

## Overview

The SIPS Connect API includes a comprehensive health check endpoint that monitors the status of all critical components and dependencies.

## Endpoint

```
GET /health
```

## Response Format

### Success Response (200 OK)

When all components are healthy:

```json
{
  "status": "ok",
  "components": [
    {
      "name": "sips-core",
      "status": "ok",
      "endpointStatus": "ok",
      "httpResult": "200 OK",
      "lastChecked": "2025-11-16T12:34:56Z",
      "errorMessage": null
    },
    {
      "name": "corebank",
      "status": "ok",
      "endpointStatus": "ok",
      "httpResult": "200 OK",
      "lastChecked": "2025-11-16T12:34:56Z",
      "errorMessage": null
    },
    {
      "name": "database",
      "status": "ok",
      "endpointStatus": "ok",
      "httpResult": "Connected",
      "lastChecked": "2025-11-16T12:34:56Z",
      "errorMessage": null
    }
  ]
}
```

### Degraded Response (503 Service Unavailable)

When one or more components are unhealthy:

```json
{
  "status": "degraded",
  "components": [
    {
      "name": "sips-core",
      "status": "ok",
      "endpointStatus": "ok",
      "httpResult": "200 OK",
      "lastChecked": "2025-11-16T12:34:56Z",
      "errorMessage": null
    },
    {
      "name": "corebank",
      "status": "degraded",
      "endpointStatus": "unreachable",
      "httpResult": "Connection Failed",
      "lastChecked": "2025-11-16T12:34:56Z",
      "errorMessage": "No connection could be made because the target machine actively refused it."
    },
    {
      "name": "database",
      "status": "ok",
      "endpointStatus": "ok",
      "httpResult": "Connected",
      "lastChecked": "2025-11-16T12:34:56Z",
      "errorMessage": null
    }
  ]
}
```

## Monitored Components

The health check monitors the following components:

### 1. SIPS Core API

- **Name:** `sips-core`
- **Configuration:** `Core:BaseUrl` in appsettings.json
- **Health Endpoint:** `{BaseUrl}/health`
- **Purpose:** Checks connectivity to the SIPS Core service

### 2. CoreBank Service

- **Name:** `corebank`
- **Configuration:** Uses first available from `ISO20022` section (Verification > Transfer > Return > Status)
- **Health Endpoint:** Derived from base URL + `/health`
- **Purpose:** Checks connectivity to the CoreBank service (only checks one endpoint to reduce risk)
- **Note:** All CoreBank endpoints typically point to the same service, so checking one is sufficient

### 3. Database

- **Name:** `database`
- **Configuration:** `ConnectionStrings:db` in appsettings.json
- **Purpose:** Verifies database connection string is configured

## Status Values

### Overall Status

- **`ok`** - All components are healthy
- **`degraded`** - One or more components are unhealthy
- **`error`** - Health check service itself failed

### Component Status

- **`ok`** - Component is healthy and responding
- **`degraded`** - Component is unhealthy or not responding

### Endpoint Status

- **`ok`** - Endpoint is reachable and responding successfully
- **`degraded`** - Endpoint returned non-success status code
- **`timeout`** - Request timed out (5 second timeout)
- **`unreachable`** - Connection failed
- **`error`** - Unexpected error occurred
- **`not-configured`** - Component is not configured

## HTTP Status Codes

- **200 OK** - All components are healthy
- **503 Service Unavailable** - One or more components are degraded or the health check failed

## Usage Examples

### cURL

```bash
# Basic health check
curl http://localhost:5000/health

# With pretty printing
curl http://localhost:5000/health | jq

# Check status code only
curl -o /dev/null -s -w "%{http_code}\n" http://localhost:5000/health
```

### PowerShell

```powershell
# Basic health check
Invoke-RestMethod -Uri "http://localhost:5000/health" -Method Get

# Check and display status
$response = Invoke-RestMethod -Uri "http://localhost:5000/health" -Method Get
Write-Host "Overall Status: $($response.status)"
$response.components | Format-Table Name, Status, HttpResult
```

### C# HttpClient

```csharp
using var client = new HttpClient();
var response = await client.GetAsync("http://localhost:5000/health");
var healthStatus = await response.Content.ReadFromJsonAsync<HealthCheckResponse>();

if (healthStatus.Status == "ok")
{
    Console.WriteLine("All systems operational");
}
else
{
    Console.WriteLine("System degraded:");
    foreach (var component in healthStatus.Components.Where(c => c.Status != "ok"))
    {
        Console.WriteLine($"  - {component.Name}: {component.ErrorMessage}");
    }
}
```

## Integration with Monitoring Tools

### Kubernetes Liveness/Readiness Probes

```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 30
  periodSeconds: 10
  timeoutSeconds: 5
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 5
  timeoutSeconds: 3
  failureThreshold: 2
```

### Docker Compose Health Check

```yaml
services:
  sips-connect:
    image: hanad/sips-connect:latest
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 40s
```

### Prometheus Monitoring

You can create alerts based on the health check endpoint:

```yaml
groups:
  - name: sips_connect_health
    rules:
      - alert: SIPSConnectDegraded
        expr: up{job="sips-connect"} == 0 or probe_http_status_code{job="sips-connect-health"} != 200
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "SIPS Connect is degraded"
          description: "SIPS Connect health check is failing"
```

## Configuration

The health check automatically discovers components from your `appsettings.json`:

```json
{
  "Core": {
    "BaseUrl": "http://sips-core:5004/api"
  },
  "ISO20022": {
    "Verification": "http://corebank:5025/api/cb/verify",
    "Transfer": "http://corebank:5025/api/CB/Transfer"
  },
  "ConnectionStrings": {
    "db": "Host=localhost;Database=SIPS.Connect.DB;..."
  }
}
```

## Timeout Configuration

Each component check has a 5-second timeout. If a component doesn't respond within this time, it will be marked as `timeout`.

## Error Handling

- Network errors are caught and reported with status `unreachable`
- Timeout errors are caught and reported with status `timeout`
- Unexpected errors are caught and reported with status `error`
- All errors include descriptive error messages in the `errorMessage` field

## Best Practices

1. **Monitor Regularly**: Set up automated monitoring to check the health endpoint every 30-60 seconds
2. **Alert on Degradation**: Configure alerts when status changes from `ok` to `degraded`
3. **Log Health Changes**: Log when components transition between states
4. **Use in Load Balancers**: Configure load balancers to remove unhealthy instances
5. **Include in CI/CD**: Verify health after deployments before routing traffic

## Troubleshooting

### Component Shows as Degraded

1. Check the `errorMessage` field for details
2. Verify the component's URL in appsettings.json
3. Ensure the component is running and accessible
4. Check network connectivity and firewall rules
5. Verify the component has a `/health` endpoint

### Health Check Times Out

1. Check if the dependent services are slow to respond
2. Verify network latency between services
3. Consider increasing timeout if services are legitimately slow
4. Check for DNS resolution issues

### Database Shows as Not Configured

1. Verify `ConnectionStrings:db` exists in appsettings.json
2. Check the connection string format
3. Ensure environment variables are properly set

## Future Enhancements

Potential improvements for the health check:

- [ ] Add actual database connectivity test (currently only checks config)
- [ ] Add response time metrics for each component
- [ ] Add configurable timeout per component
- [ ] Add detailed version information
- [ ] Add memory and CPU usage metrics
- [ ] Add disk space monitoring
- [ ] Add custom health checks for business logic
- [ ] Add health check history/trends
