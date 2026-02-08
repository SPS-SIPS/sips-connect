# Live Participants API - Integration Guide

## Overview

This guide is for external banking systems and services that need to query the real-time availability status of financial institutions participating in the payment switch network. The Live Participants API provides instant access to cached participant status without requiring complex monitoring infrastructure.

## Use Cases

- **Pre-transaction validation** - Check if recipient bank is online before initiating payment
- **Routing decisions** - Route transactions only to available institutions
- **Dashboard displays** - Show real-time network status
- **Automated failover** - Redirect traffic when primary institution is offline
- **SLA monitoring** - Track participant availability for compliance

## API Endpoint

### Base URL

```
Production: https://api.sps.so/api/v1
Staging: https://staging-api.sps.so/api/v1
Development: http://localhost:5007/api/v1
```

### Endpoint

```
GET /participants/live
```

## Authentication

The API uses JWT Bearer token authentication.

### Obtaining Access Token

Contact your SPS System administrator to obtain API credentials. Once you have credentials, authenticate to receive a JWT token:

```bash
POST /auth/login
Content-Type: application/json

{
  "username": "your_username",
  "password": "your_password"
}
```

**Response:**
```json
{
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "...",
    "expiresIn": 1200
  },
  "succeeded": true
}
```

### Using the Token

Include the token in the `Authorization` header for all API requests:

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

## Request Format

### Get All Participants with Status

```http
GET /api/v1/participants/live
Authorization: Bearer {your_token}
```

### Get Only Live Participants

```http
GET /api/v1/participants/live?IsLive=true
Authorization: Bearer {your_token}
```

### Get Only Offline Participants

```http
GET /api/v1/participants/live?IsLive=false
Authorization: Bearer {your_token}
```

## Response Format

### Success Response

**HTTP Status:** 200 OK

```json
{
  "data": [
    {
      "institutionBic": "AGROSOS0",
      "institutionName": "AGRO Bank UAT",
      "isLive": true,
      "lastCheckedAt": "2025-12-29T09:20:00Z",
      "lastStatusChangeAt": "2025-12-29T08:15:00Z",
      "consecutiveFailures": 0,
      "consecutiveSuccesses": 5,
      "lastError": null
    },
    {
      "institutionBic": "ZKBASOS0",
      "institutionName": "Zirat Bank UAT",
      "isLive": false,
      "lastCheckedAt": "2025-12-29T09:20:00Z",
      "lastStatusChangeAt": "2025-12-29T09:10:00Z",
      "consecutiveFailures": 4,
      "consecutiveSuccesses": 0,
      "lastError": "connection refused"
    }
  ],
  "succeeded": true,
  "message": null,
  "errors": null
}
```

### Response Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `institutionBic` | string | Yes | Bank Identifier Code (e.g., "AGROSOS0") |
| `institutionName` | string | Yes | Full name of the institution |
| `isLive` | boolean | Yes | Current availability status (true = online, false = offline) |
| `lastCheckedAt` | ISO 8601 datetime | Yes | When the last health check was performed (UTC) |
| `lastStatusChangeAt` | ISO 8601 datetime | Yes | When the status last changed (UTC) |
| `consecutiveFailures` | integer | Yes | Number of consecutive failed health checks |
| `consecutiveSuccesses` | integer | Yes | Number of consecutive successful health checks |
| `lastError` | string | No | Most recent error message (null if no error) |

### Error Responses

#### 401 Unauthorized
```json
{
  "data": null,
  "succeeded": false,
  "message": "Unauthorized",
  "errors": ["Invalid or expired token"]
}
```

#### 403 Forbidden
```json
{
  "data": null,
  "succeeded": false,
  "message": "Forbidden",
  "errors": ["Insufficient permissions"]
}
```

#### 500 Internal Server Error
```json
{
  "data": null,
  "succeeded": false,
  "message": "Internal server error",
  "errors": ["An unexpected error occurred"]
}
```

## Integration Examples

### JavaScript/TypeScript (Node.js)

```javascript
const axios = require('axios');

class LiveParticipantsClient {
  constructor(baseUrl, token) {
    this.baseUrl = baseUrl;
    this.token = token;
  }

  async getLiveParticipants(isLive = null) {
    try {
      const params = isLive !== null ? { IsLive: isLive } : {};
      const response = await axios.get(
        `${this.baseUrl}/api/v1/participants/live`,
        {
          headers: {
            'Authorization': `Bearer ${this.token}`
          },
          params
        }
      );
      
      return response.data.data;
    } catch (error) {
      console.error('Error fetching live participants:', error.message);
      throw error;
    }
  }

  async isParticipantLive(bic) {
    const participants = await this.getLiveParticipants(true);
    return participants.some(p => p.institutionBic === bic);
  }

  async getAvailableParticipants() {
    return await this.getLiveParticipants(true);
  }
}

// Usage
const client = new LiveParticipantsClient(
  'https://api.sps.so',
  'your_jwt_token'
);

// Check if specific bank is live
const isLive = await client.isParticipantLive('AGROSOS0');
console.log(`AGRO Bank is ${isLive ? 'online' : 'offline'}`);

// Get all live participants
const liveParticipants = await client.getAvailableParticipants();
console.log(`${liveParticipants.length} participants are currently online`);
```

### Python

```python
import requests
from typing import List, Optional, Dict
from datetime import datetime

class LiveParticipantsClient:
    def __init__(self, base_url: str, token: str):
        self.base_url = base_url
        self.headers = {
            'Authorization': f'Bearer {token}',
            'Content-Type': 'application/json'
        }
    
    def get_live_participants(self, is_live: Optional[bool] = None) -> List[Dict]:
        """Get participants with optional live status filter"""
        url = f"{self.base_url}/api/v1/participants/live"
        params = {}
        if is_live is not None:
            params['IsLive'] = str(is_live).lower()
        
        response = requests.get(url, headers=self.headers, params=params)
        response.raise_for_status()
        
        return response.json()['data']
    
    def is_participant_live(self, bic: str) -> bool:
        """Check if a specific participant is live"""
        participants = self.get_live_participants(is_live=True)
        return any(p['institutionBic'] == bic for p in participants)
    
    def get_available_participants(self) -> List[str]:
        """Get list of BICs for all live participants"""
        participants = self.get_live_participants(is_live=True)
        return [p['institutionBic'] for p in participants]

# Usage
client = LiveParticipantsClient(
    base_url='https://api.sps.so',
    token='your_jwt_token'
)

# Check if specific bank is live
if client.is_participant_live('AGROSOS0'):
    print("AGRO Bank is online - proceed with transaction")
else:
    print("AGRO Bank is offline - use alternative route")

# Get all live participants
live_bics = client.get_available_participants()
print(f"Available institutions: {', '.join(live_bics)}")
```

### Java (Spring Boot)

```java
import org.springframework.http.*;
import org.springframework.web.client.RestTemplate;
import org.springframework.web.util.UriComponentsBuilder;

public class LiveParticipantsClient {
    private final String baseUrl;
    private final String token;
    private final RestTemplate restTemplate;

    public LiveParticipantsClient(String baseUrl, String token) {
        this.baseUrl = baseUrl;
        this.token = token;
        this.restTemplate = new RestTemplate();
    }

    public List<ParticipantStatus> getLiveParticipants(Boolean isLive) {
        HttpHeaders headers = new HttpHeaders();
        headers.setBearerAuth(token);
        HttpEntity<?> entity = new HttpEntity<>(headers);

        UriComponentsBuilder builder = UriComponentsBuilder
            .fromHttpUrl(baseUrl + "/api/v1/participants/live");
        
        if (isLive != null) {
            builder.queryParam("IsLive", isLive);
        }

        ResponseEntity<ApiResponse<List<ParticipantStatus>>> response = 
            restTemplate.exchange(
                builder.toUriString(),
                HttpMethod.GET,
                entity,
                new ParameterizedTypeReference<ApiResponse<List<ParticipantStatus>>>() {}
            );

        return response.getBody().getData();
    }

    public boolean isParticipantLive(String bic) {
        List<ParticipantStatus> liveParticipants = getLiveParticipants(true);
        return liveParticipants.stream()
            .anyMatch(p -> p.getInstitutionBic().equals(bic));
    }
}

// DTOs
@Data
public class ParticipantStatus {
    private String institutionBic;
    private String institutionName;
    private boolean isLive;
    private String lastCheckedAt;
    private String lastStatusChangeAt;
    private int consecutiveFailures;
    private int consecutiveSuccesses;
    private String lastError;
}

@Data
public class ApiResponse<T> {
    private T data;
    private boolean succeeded;
    private String message;
    private List<String> errors;
}
```

### C# (.NET)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

public class LiveParticipantsClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public LiveParticipantsClient(string baseUrl, string token)
    {
        _baseUrl = baseUrl;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<List<ParticipantStatus>> GetLiveParticipantsAsync(bool? isLive = null)
    {
        var url = $"{_baseUrl}/api/v1/participants/live";
        if (isLive.HasValue)
        {
            url += $"?IsLive={isLive.Value.ToString().ToLower()}";
        }

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<ParticipantStatus>>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        return apiResponse.Data;
    }

    public async Task<bool> IsParticipantLiveAsync(string bic)
    {
        var liveParticipants = await GetLiveParticipantsAsync(isLive: true);
        return liveParticipants.Any(p => p.InstitutionBic == bic);
    }

    public async Task<List<string>> GetAvailableParticipantsAsync()
    {
        var liveParticipants = await GetLiveParticipantsAsync(isLive: true);
        return liveParticipants.Select(p => p.InstitutionBic).ToList();
    }
}

// DTOs
public class ParticipantStatus
{
    public string InstitutionBic { get; set; }
    public string InstitutionName { get; set; }
    public bool IsLive { get; set; }
    public DateTimeOffset LastCheckedAt { get; set; }
    public DateTimeOffset LastStatusChangeAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public string LastError { get; set; }
}

public class ApiResponse<T>
{
    public T Data { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; }
    public List<string> Errors { get; set; }
}
```

### cURL

```bash
# Get all participants
curl -X GET "https://api.sps.so/api/v1/participants/live" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json"

# Get only live participants
curl -X GET "https://api.sps.so/api/v1/participants/live?IsLive=true" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json"

# Get only offline participants
curl -X GET "https://api.sps.so/api/v1/participants/live?IsLive=false" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json"
```

## Best Practices

### 1. Caching Strategy

The API returns cached data that is updated every 2 minutes. You can implement your own caching layer:

```javascript
class CachedLiveParticipantsClient {
  constructor(client, cacheDurationMs = 60000) { // 1 minute cache
    this.client = client;
    this.cacheDurationMs = cacheDurationMs;
    this.cache = null;
    this.lastFetch = null;
  }

  async getLiveParticipants(isLive = null) {
    const now = Date.now();
    
    if (this.cache && this.lastFetch && 
        (now - this.lastFetch) < this.cacheDurationMs) {
      return this.filterCache(isLive);
    }

    this.cache = await this.client.getLiveParticipants();
    this.lastFetch = now;
    
    return this.filterCache(isLive);
  }

  filterCache(isLive) {
    if (isLive === null) return this.cache;
    return this.cache.filter(p => p.isLive === isLive);
  }
}
```

### 2. Error Handling

Always implement proper error handling:

```python
def get_live_participants_with_retry(client, max_retries=3):
    for attempt in range(max_retries):
        try:
            return client.get_live_participants(is_live=True)
        except requests.exceptions.RequestException as e:
            if attempt == max_retries - 1:
                raise
            time.sleep(2 ** attempt)  # Exponential backoff
```

### 3. Pre-Transaction Validation

```javascript
async function validateRecipient(recipientBic, client) {
  try {
    const isLive = await client.isParticipantLive(recipientBic);
    
    if (!isLive) {
      return {
        valid: false,
        reason: 'Recipient institution is currently offline'
      };
    }
    
    return { valid: true };
  } catch (error) {
    // Fail open - allow transaction if status check fails
    console.warn('Could not verify recipient status:', error);
    return { valid: true, warning: 'Status verification unavailable' };
  }
}
```

### 4. Monitoring Integration

```python
import time
from prometheus_client import Gauge

# Prometheus metrics
live_participants_gauge = Gauge(
    'payment_switch_live_participants',
    'Number of live participants in payment switch'
)

def update_metrics(client):
    while True:
        try:
            live_count = len(client.get_live_participants(is_live=True))
            live_participants_gauge.set(live_count)
        except Exception as e:
            print(f"Error updating metrics: {e}")
        
        time.sleep(60)  # Update every minute
```

## Rate Limiting

- **Rate Limit:** 100 requests per minute per API key
- **Burst Limit:** 10 requests per second

If you exceed the rate limit, you'll receive a `429 Too Many Requests` response:

```json
{
  "data": null,
  "succeeded": false,
  "message": "Rate limit exceeded",
  "errors": ["Too many requests. Please try again later."]
}
```

## Data Freshness

- Status is updated every **2 minutes** by the background worker
- Health checks are performed every **1 minute** by the monitoring service
- Maximum data staleness: **2 minutes**

## Support & Contact

For API access, technical support, or to report issues:

- **Email:** api-support@sps.so
- **Technical Documentation:** https://docs.sps.so
- **Status Page:** https://status.sps.so

## Changelog

### Version 1.0 (December 2025)
- Initial release
- Live participants endpoint
- JWT authentication
- Real-time status caching

## Appendix: Status Determination Logic

The system uses intelligent thresholds to determine participant status:

- **Transition to LIVE:** Requires 2+ consecutive successful health checks
- **Transition to OFFLINE:** Requires 3+ consecutive failed health checks
- **Maintain Status:** If thresholds not met, status remains unchanged

This prevents status "flapping" due to temporary network issues while ensuring quick detection of actual outages.

## Security Considerations

1. **Token Security:** Store JWT tokens securely, never in source code
2. **HTTPS Only:** Always use HTTPS in production
3. **Token Rotation:** Refresh tokens before expiration
4. **IP Whitelisting:** Request IP whitelisting for production systems
5. **Audit Logging:** All API calls are logged for security auditing

## SLA & Availability

- **API Uptime:** 99.9% guaranteed
- **Response Time:** < 100ms (95th percentile)
- **Data Freshness:** < 2 minutes
- **Support Response:** < 4 hours for critical issues
