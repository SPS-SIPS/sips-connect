# SIPS Connect API - Integration Guide

## Overview

This guide provides comprehensive documentation for integrating with the SIPS Connect API to access real-time participant status and balance information. The API enables external systems to query the availability and financial status of participating institutions in the payment network.

## Table of Contents

- [Authentication](#authentication)
- [Live Participants Endpoint](#live-participants-endpoint)
- [Balance Status Endpoint](#balance-status-endpoint)
- [Error Handling](#error-handling)
- [Best Practices](#best-practices)
- [Code Examples](#code-examples)

---

## Authentication

All API endpoints require API Key authentication.

### Base URLs

```
Production: https://your-production-domain.com/api/v1
Staging: https://your-staging-domain.com/api/v1
Development: https://localhost/api/v1
```

### Obtaining API Credentials

Contact your SIPS Connect administrator to obtain API credentials. You will receive:
- **API Key** - Your unique identifier
- **API Secret** - Your secret key for authentication

**Important:** Keep your API Secret secure and never expose it in client-side code or public repositories.

### Using API Key Authentication

Include your API Key in the `X-API-Key` header for all API requests:

```http
X-API-Key: your_api_key_here
```

**Example Request:**
```http
GET /api/v1/participants/live HTTP/1.1
Host: your-api-domain.com
X-API-Key: your_api_key_here
```

### Authentication Method

SIPS Connect uses API Key authentication which does not require login or token refresh. Your API Key remains valid until revoked by an administrator.

---

## Live Participants Endpoint

Query the real-time availability status of all participating financial institutions in the payment network.

### Endpoint

```
GET /api/v1/participants/live
```

### Request Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `isLive` | boolean | No | Filter by availability status. `true` for online only, `false` for offline only, omit for all participants |

### Request Examples

**Get all participants:**
```http
GET /api/v1/participants/live HTTP/1.1
Host: your-api-domain.com
X-API-Key: your_api_key_here
```

**Get only online participants:**
```http
GET /api/v1/participants/live?isLive=true HTTP/1.1
Host: your-api-domain.com
X-API-Key: your_api_key_here
```

**Get only offline participants:**
```http
GET /api/v1/participants/live?isLive=false HTTP/1.1
Host: your-api-domain.com
X-API-Key: your_api_key_here
```

### Response Format

**HTTP Status:** `200 OK`

```json
{
  "data": [
    {
      "institutionBic": "AGROSOS0",
      "institutionName": "AGRO Bank UAT",
      "isLive": true,
      "lastCheckedAt": "2026-01-03T11:05:30.123456Z",
      "lastStatusChangeAt": "2026-01-03T08:00:00.000000Z",
      "consecutiveFailures": 0,
      "consecutiveSuccesses": 150,
      "lastError": null,
      "currentBalance": 15000.50,
      "availableBalance": 14500.50,
      "canSend": true,
      "minimumSendBalance": 1000.00
    },
    {
      "institutionBic": "TESTBANK",
      "institutionName": "Test Bank",
      "isLive": true,
      "lastCheckedAt": "2026-01-03T11:05:30.123456Z",
      "lastStatusChangeAt": "2026-01-03T10:30:00.000000Z",
      "consecutiveFailures": 0,
      "consecutiveSuccesses": 25,
      "lastError": null,
      "currentBalance": 500.00,
      "availableBalance": -500.00,
      "canSend": false,
      "minimumSendBalance": 1000.00
    },
    {
      "institutionBic": "OFFBANK",
      "institutionName": "Offline Bank",
      "isLive": false,
      "lastCheckedAt": "2026-01-03T11:05:30.123456Z",
      "lastStatusChangeAt": "2026-01-03T09:15:00.000000Z",
      "consecutiveFailures": 3,
      "consecutiveSuccesses": 0,
      "lastError": "Connection timeout after 5000ms",
      "currentBalance": null,
      "availableBalance": null,
      "canSend": null,
      "minimumSendBalance": null
    }
  ],
  "succeeded": true,
  "message": null,
  "errors": null
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `institutionBic` | string | Bank Identifier Code (BIC/SWIFT code) |
| `institutionName` | string | Full name of the financial institution |
| `isLive` | boolean | Current availability status. `true` = online, `false` = offline |
| `lastCheckedAt` | datetime | Timestamp of the most recent health check (ISO 8601 format) |
| `lastStatusChangeAt` | datetime | Timestamp when status last changed between online/offline |
| `consecutiveFailures` | integer | Number of consecutive failed health checks |
| `consecutiveSuccesses` | integer | Number of consecutive successful health checks |
| `lastError` | string/null | Error message from the last failed health check, `null` if no error |
| `currentBalance` | decimal/null | Current balance of the participant. `null` if unavailable |
| `availableBalance` | decimal/null | Available balance (current balance minus reserved funds). Can be negative |
| `canSend` | boolean/null | Whether the participant can send payments. `false` if balance is below minimum |
| `minimumSendBalance` | decimal/null | Minimum balance required to send payments |

### Balance and Send Restrictions

The API now includes balance information to help determine if a participant can send payments:

- **`canSend: true`** - Participant can send payments (balance above minimum threshold)
- **`canSend: false`** - Participant is blocked from sending (balance below minimum threshold)
- **`canSend: null`** - Balance information not available (participant may be offline)

**Important:** Even if `isLive: true`, a participant may have `canSend: false` due to insufficient balance. Always check both fields before routing payments.

### Caching

The live participants data is cached for **5 minutes** by default. Subsequent requests within this period will return cached data for optimal performance.

### Use Cases

1. **Pre-transaction Validation** - Check if recipient bank is online and can receive payments
2. **Payment Routing** - Route transactions only to available institutions with sufficient balance
3. **Dashboard Displays** - Show real-time network status to operators
4. **Automated Failover** - Redirect traffic when primary institution is offline or cannot send
5. **Balance Monitoring** - Track participant balances for risk management

---

## Balance Status Endpoint

Query the detailed balance status of a specific participant (typically your own institution).

### Endpoint

```
GET /api/v1/participants/balance-status
```

### Request

```http
GET /api/v1/participants/balance-status HTTP/1.1
Host: your-api-domain.com
X-API-Key: your_api_key_here
```

**Note:** The endpoint returns balance information for your institution based on the API Key's associated tenant.

### Response Format

**HTTP Status:** `200 OK`

```json
{
  "data": {
    "bic": "ZKBASOS0",
    "participantName": "Zirat Bank",
    "lastKnownBalance": 775639.30,
    "currentZone": "Green",
    "lastZoneChangeAt": null,
    "lastAlertSentAt": null,
    "lastUpdatedAt": "2026-01-03T11:00:00.123456Z",
    "currency": "USD"
  },
  "succeeded": true,
  "message": null,
  "errors": null
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `bic` | string | Bank Identifier Code of the participant |
| `participantName` | string | Full name of the participant institution |
| `lastKnownBalance` | decimal | Most recent balance amount |
| `currentZone` | string | Balance zone indicator: `"Green"`, `"Yellow"`, or `"Red"` |
| `lastZoneChangeAt` | datetime/null | When the zone last changed, `null` if never changed |
| `lastAlertSentAt` | datetime/null | When the last balance alert was sent, `null` if no alerts |
| `lastUpdatedAt` | datetime | Timestamp of the last balance update (ISO 8601 format) |
| `currency` | string | Currency code (e.g., "USD", "EUR") |

### Balance Zones

The `currentZone` field indicates the health of your balance:

- **Green** - Balance is healthy and above all thresholds
- **Yellow** - Balance is approaching minimum threshold (warning state)
- **Red** - Balance is below minimum threshold (critical state)

### Use Cases

1. **Balance Monitoring** - Track your institution's current balance in real-time
2. **Alert Systems** - Trigger notifications when balance enters Yellow or Red zones
3. **Dashboard Widgets** - Display balance status to operators and management
4. **Automated Top-ups** - Initiate fund transfers when balance is low
5. **Compliance Reporting** - Generate balance reports for regulatory requirements

---

## Error Handling

### HTTP Status Codes

| Status Code | Description |
|-------------|-------------|
| `200 OK` | Request successful |
| `400 Bad Request` | Invalid request parameters |
| `401 Unauthorized` | Missing or invalid authentication token |
| `403 Forbidden` | Insufficient permissions to access the resource |
| `404 Not Found` | Endpoint not found |
| `500 Internal Server Error` | Server-side error occurred |
| `503 Service Unavailable` | Service temporarily unavailable |

### Error Response Format

```json
{
  "data": null,
  "succeeded": false,
  "message": "Error description",
  "errors": [
    "Detailed error message 1",
    "Detailed error message 2"
  ]
}
```

### Common Error Scenarios

#### 401 Unauthorized
```json
{
  "data": null,
  "succeeded": false,
  "message": "Unauthorized",
  "errors": ["Invalid or missing API Key"]
}
```

**Solution:** Verify your API Key is correct and included in the `X-API-Key` header.

#### 403 Forbidden
```json
{
  "data": null,
  "succeeded": false,
  "message": "Forbidden",
  "errors": ["You are not authorized to perform this action"]
}
```

**Solution:** Contact your administrator to verify your account has the required permissions.

#### 500 Internal Server Error
```json
{
  "data": null,
  "succeeded": false,
  "message": "Internal server error",
  "errors": ["An unexpected error occurred"]
}
```

**Solution:** Retry the request. If the error persists, contact support.

---

## Best Practices

### 1. API Key Management

- **Secure storage** - Store API Keys securely using environment variables or secrets management
- **Never expose keys** - Never commit API Keys to source control or expose in client-side code
- **Handle 401 errors** - Verify API Key is correct when receiving unauthorized responses
- **Key rotation** - Contact your administrator if you need to rotate your API Key

### 2. Rate Limiting

- **Respect cache duration** - Live participants data is cached for 5 minutes
- **Avoid excessive polling** - Query endpoints only when needed
- **Implement exponential backoff** - If requests fail, wait before retrying

### 3. Error Handling

- **Implement retry logic** - Retry failed requests with exponential backoff
- **Log errors** - Capture and log all API errors for debugging
- **Graceful degradation** - Handle API unavailability gracefully in your application
- **Validate responses** - Always check the `succeeded` field before processing data

### 4. Data Processing

- **Check both `isLive` and `canSend`** - A participant may be online but unable to send payments
- **Handle null values** - Balance fields may be `null` for offline participants
- **Monitor zone changes** - Track `currentZone` changes for balance alerts
- **Cache locally** - Cache API responses appropriately to reduce load

### 5. Security

- **Use HTTPS** - Always use HTTPS in production environments
- **Validate certificates** - Ensure SSL/TLS certificates are valid
- **Protect credentials** - Store API credentials securely (use environment variables or secrets management)
- **Implement timeouts** - Set reasonable timeouts for API requests (recommended: 30 seconds)

---

## Code Examples

### C# / .NET

```csharp
using System.Text.Json;

public class SipsConnectClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public SipsConnectClient(string baseUrl, string apiKey)
    {
        _httpClient = new HttpClient();
        _baseUrl = baseUrl;
        _apiKey = apiKey;
        
        // Set API Key header for all requests
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
    }

    // Get live participants
    public async Task<List<ParticipantStatus>?> GetLiveParticipantsAsync(bool? isLive = null)
    {
        var url = $"{_baseUrl}/participants/live";
        if (isLive.HasValue)
            url += $"?isLive={isLive.Value.ToString().ToLower()}";

        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<ParticipantStatus>>>(content);
        
        return apiResponse?.Data;
    }

    // Get balance status
    public async Task<BalanceStatus?> GetBalanceStatusAsync()
    {
        var url = $"{_baseUrl}/participants/balance-status";
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<ApiResponse<BalanceStatus>>(content);
        
        return apiResponse?.Data;
    }
}

// Usage example
var apiKey = Environment.GetEnvironmentVariable("SIPS_API_KEY") ?? "your_api_key_here";
var client = new SipsConnectClient("https://your-api-domain.com/api/v1", apiKey);

// Get all live participants
var allParticipants = await client.GetLiveParticipantsAsync();

// Get only online participants
var onlineParticipants = await client.GetLiveParticipantsAsync(isLive: true);

// Get balance status
var balanceStatus = await client.GetBalanceStatusAsync();

// Check if a specific participant can receive payments
var recipient = allParticipants?.FirstOrDefault(p => p.InstitutionBic == "AGROSOS0");
if (recipient?.IsLive == true && recipient?.CanSend != false)
{
    Console.WriteLine($"{recipient.InstitutionName} can receive payments");
}
else
{
    Console.WriteLine($"{recipient?.InstitutionName} cannot receive payments");
}
```

### Python

```python
import requests
import os
from typing import Optional, List, Dict

class SipsConnectClient:
    def __init__(self, base_url: str, api_key: str):
        self.base_url = base_url
        self.api_key = api_key
        self.session = requests.Session()
        
        # Set API Key header for all requests
        self.session.headers.update({"X-API-Key": api_key})

    def get_live_participants(self, is_live: Optional[bool] = None) -> Optional[List[Dict]]:
        """Get live participants status"""
        url = f"{self.base_url}/participants/live"
        
        params = {}
        if is_live is not None:
            params["isLive"] = str(is_live).lower()
        
        response = self.session.get(url, params=params)
        
        if response.status_code != 200:
            return None
        
        data = response.json()
        return data.get("data")

    def get_balance_status(self) -> Optional[Dict]:
        """Get balance status"""
        url = f"{self.base_url}/participants/balance-status"
        
        response = self.session.get(url)
        
        if response.status_code != 200:
            return None
        
        data = response.json()
        return data.get("data")

# Usage example
api_key = os.getenv("SIPS_API_KEY", "your_api_key_here")
client = SipsConnectClient("https://your-api-domain.com/api/v1", api_key)

# Get all participants
all_participants = client.get_live_participants()

# Get only online participants
online_participants = client.get_live_participants(is_live=True)

# Get balance status
balance_status = client.get_balance_status()

# Check if a specific participant can receive payments
if all_participants:
    for participant in all_participants:
        if participant["institutionBic"] == "AGROSOS0":
            if participant["isLive"] and participant.get("canSend") != False:
                print(f"{participant['institutionName']} can receive payments")
            else:
                print(f"{participant['institutionName']} cannot receive payments")
```

### JavaScript / Node.js

```javascript
const axios = require('axios');

class SipsConnectClient {
    constructor(baseUrl, apiKey) {
        this.baseUrl = baseUrl;
        this.apiKey = apiKey;
        this.client = axios.create({
            timeout: 30000,
            headers: {
                'X-API-Key': apiKey
            }
        });
    }

    // Get live participants
    async getLiveParticipants(isLive = null) {
        try {
            const url = `${this.baseUrl}/participants/live`;
            const params = {};
            
            if (isLive !== null) {
                params.isLive = isLive;
            }

            const response = await this.client.get(url, { params });
            return response.data.data;
        } catch (error) {
            console.error('Failed to get live participants:', error.message);
            return null;
        }
    }

    // Get balance status
    async getBalanceStatus() {
        try {
            const url = `${this.baseUrl}/participants/balance-status`;
            const response = await this.client.get(url);
            return response.data.data;
        } catch (error) {
            console.error('Failed to get balance status:', error.message);
            return null;
        }
    }
}

// Usage example
(async () => {
    const apiKey = process.env.SIPS_API_KEY || 'your_api_key_here';
    const client = new SipsConnectClient('https://your-api-domain.com/api/v1', apiKey);

    // Get all participants
    const allParticipants = await client.getLiveParticipants();
    console.log('All participants:', allParticipants);

    // Get only online participants
    const onlineParticipants = await client.getLiveParticipants(true);
    console.log('Online participants:', onlineParticipants);

    // Get balance status
    const balanceStatus = await client.getBalanceStatus();
    console.log('Balance status:', balanceStatus);

    // Check if a specific participant can receive payments
    const recipient = allParticipants?.find(p => p.institutionBic === 'AGROSOS0');
    if (recipient?.isLive && recipient?.canSend !== false) {
        console.log(`${recipient.institutionName} can receive payments`);
    } else {
        console.log(`${recipient?.institutionName} cannot receive payments`);
    }
})();
```

### cURL Examples

**Get all live participants:**
```bash
curl -X GET https://your-api-domain.com/api/v1/participants/live \
  -H "X-API-Key: your_api_key_here"
```

**Get only online participants:**
```bash
curl -X GET "https://your-api-domain.com/api/v1/participants/live?isLive=true" \
  -H "X-API-Key: your_api_key_here"
```

**Get only offline participants:**
```bash
curl -X GET "https://your-api-domain.com/api/v1/participants/live?isLive=false" \
  -H "X-API-Key: your_api_key_here"
```

**Get balance status:**
```bash
curl -X GET https://your-api-domain.com/api/v1/participants/balance-status \
  -H "X-API-Key: your_api_key_here"
```

---

## Support

For technical support, questions, or to report issues:

- **Email:** support@your-organization.com
- **Documentation:** https://docs.your-organization.com
- **Issue Tracker:** https://github.com/your-org/sips-connect/issues

---

## Changelog

### Version 1.4.10 (2026-01-03)
- Added balance information to live participants endpoint
- Added `currentBalance`, `availableBalance`, `canSend`, and `minimumSendBalance` fields
- Implemented send restriction logic based on participant balance

### Version 1.4.9 (2025-12-31)
- Fixed authentication to use decrypted credentials from CoreOptions
- Improved error logging for authentication failures

### Version 1.4.8 (2025-12-31)
- Added explicit JWT authentication for balance status endpoint
- Created CoreAuthService for centralized authentication management

### Version 1.4.7 (2025-12-30)
- Initial release of balance status endpoint
- Added participant balance monitoring functionality

---

## License

Copyright © 2026 Your Organization. All rights reserved.
