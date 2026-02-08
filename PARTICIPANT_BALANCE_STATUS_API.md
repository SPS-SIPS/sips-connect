# Participant Balance Status API - Client Implementation Guide

## Overview
This document provides comprehensive guidance for client applications to integrate with the Participant Balance Status API endpoint. This endpoint allows authenticated participants to retrieve their current balance information and threshold zone status in real-time.

## Table of Contents
1. [Endpoint Details](#endpoint-details)
2. [Authentication](#authentication)
3. [Request Format](#request-format)
4. [Response Format](#response-format)
5. [Threshold Zones Explained](#threshold-zones-explained)
6. [Error Handling](#error-handling)
7. [Implementation Examples](#implementation-examples)
8. [Best Practices](#best-practices)
9. [Troubleshooting](#troubleshooting)

---

## Endpoint Details

### Base Information
- **Endpoint:** `/api/v1/participants/balance-status`
- **Method:** `GET`
- **Content-Type:** `application/json`
- **Authentication:** Required (JWT Bearer Token)
- **Authorization:** Participant must have valid authentication token

### Endpoint URL
```
GET https://{api-host}/api/v1/participants/balance-status
```

---

## Authentication

### JWT Bearer Token
All requests must include a valid JWT bearer token in the Authorization header. The token contains the participant's BIC (Bank Identifier Code) which is used to identify and retrieve the correct balance information.

**Header Format:**
```http
Authorization: Bearer {your-jwt-token}
```

### Token Claims
Your JWT token must contain the following claim:
- `tenantId`: Your organization's BIC code (11 characters)

### Obtaining a Token
Contact your system administrator to obtain authentication credentials. Typically, you'll authenticate via a login endpoint and receive a JWT token in response.

---

## Request Format

### HTTP Request
```http
GET /api/v1/participants/balance-status HTTP/1.1
Host: {api-host}
Authorization: Bearer {your-jwt-token}
Accept: application/json
```

### cURL Example
```bash
curl -X GET "https://{api-host}/api/v1/participants/balance-status" \
  -H "Authorization: Bearer {your-jwt-token}" \
  -H "Accept: application/json"
```

### No Request Body Required
This endpoint does not require any request body or query parameters. The participant is automatically identified from the JWT token.

---

## Response Format

### Success Response (HTTP 200)

```json
{
  "data": {
    "bic": "AALLSOS0",
    "participantName": "Amal Bank UAT",
    "lastKnownBalance": 75000.50,
    "currentZone": "Green",
    "lastZoneChangeAt": "2025-12-30T08:15:30.123Z",
    "lastAlertSentAt": null,
    "lastUpdatedAt": "2025-12-30T12:45:00.000Z",
    "currency": "USD"
  },
  "error": null,
  "httpStatusCode": 200
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `bic` | string | Your organization's Bank Identifier Code (11 characters) |
| `participantName` | string | Your organization's registered name |
| `lastKnownBalance` | decimal (nullable) | Current remaining net debit cap balance. `null` if no balance data received yet |
| `currentZone` | string | Current threshold zone: `"Green"`, `"Amber"`, or `"Red"` |
| `lastZoneChangeAt` | datetime (nullable) | ISO 8601 timestamp of the last zone transition. `null` if zone never changed |
| `lastAlertSentAt` | datetime (nullable) | ISO 8601 timestamp when the last alert email was sent. `null` if no alerts sent |
| `lastUpdatedAt` | datetime (nullable) | ISO 8601 timestamp of the last balance update. `null` if no data received |
| `currency` | string | Currency code (typically "USD") |

---

## Threshold Zones Explained

### Zone Definitions

The system monitors your balance against configured thresholds and categorizes your status into three zones:

#### 🟢 **Green Zone** - Healthy
- **Meaning:** Your balance is above the amber threshold
- **Status:** Normal operations, no concerns
- **Action Required:** None
- **Alerts:** No alerts sent

#### 🟡 **Amber Zone** - Warning
- **Meaning:** Your balance has fallen below the amber threshold but is still above the red threshold
- **Status:** Caution - monitor your balance closely
- **Action Required:** Consider adding funds or reducing transaction volume
- **Alerts:** Email alert sent when entering this zone

#### 🔴 **Red Zone** - Critical
- **Meaning:** Your balance has fallen below the red threshold
- **Status:** Critical - immediate action required
- **Action Required:** Add funds immediately to avoid service disruption
- **Alerts:** Email alert sent when entering this zone

### Zone Calculation Example

If your configuration is:
- **Golden Amount:** $100,000
- **Amber Percentage:** 60%
- **Red Percentage:** 30%

Then your thresholds are:
- **Amber Threshold:** $60,000 (60% of $100,000)
- **Red Threshold:** $30,000 (30% of $100,000)

**Zone Assignment:**
- Balance ≥ $60,000 → **Green Zone**
- $30,000 ≤ Balance < $60,000 → **Amber Zone**
- Balance < $30,000 → **Red Zone**

---

## Error Handling

### Error Response Format

```json
{
  "data": null,
  "error": "Error message description",
  "httpStatusCode": 404
}
```

### Common Error Responses

#### 401 Unauthorized
```json
{
  "data": null,
  "error": "Unauthorized",
  "httpStatusCode": 401
}
```
**Cause:** Missing or invalid JWT token
**Solution:** Ensure you're sending a valid bearer token in the Authorization header

#### 404 Not Found
```json
{
  "data": null,
  "error": "No threshold configuration found for BIC XXXXX",
  "httpStatusCode": 404
}
```
**Cause:** No threshold configuration exists for your BIC
**Solution:** Contact your system administrator to configure thresholds for your organization

#### 500 Internal Server Error
```json
{
  "data": null,
  "error": "An error occurred while processing your request",
  "httpStatusCode": 500
}
```
**Cause:** Server-side error
**Solution:** Retry the request. If the issue persists, contact technical support

---

## Implementation Examples

### JavaScript (Fetch API)

```javascript
async function getBalanceStatus(token) {
  try {
    const response = await fetch('https://{api-host}/api/v1/participants/balance-status', {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Accept': 'application/json'
      }
    });

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }

    const result = await response.json();
    
    if (result.data) {
      console.log('BIC:', result.data.bic);
      console.log('Balance:', result.data.lastKnownBalance);
      console.log('Zone:', result.data.currentZone);
      
      // Handle zone-specific logic
      switch(result.data.currentZone) {
        case 'Green':
          console.log('✓ Balance is healthy');
          break;
        case 'Amber':
          console.warn('⚠ Balance is in warning zone');
          break;
        case 'Red':
          console.error('✗ Balance is critical!');
          break;
      }
      
      return result.data;
    }
  } catch (error) {
    console.error('Error fetching balance status:', error);
    throw error;
  }
}

// Usage
const token = 'your-jwt-token-here';
getBalanceStatus(token)
  .then(data => console.log('Balance Status:', data))
  .catch(error => console.error('Failed:', error));
```

### Python (requests library)

```python
import requests
from datetime import datetime

def get_balance_status(api_host, token):
    """
    Fetch participant balance status from the API.
    
    Args:
        api_host (str): API host URL
        token (str): JWT bearer token
        
    Returns:
        dict: Balance status data
    """
    url = f"https://{api_host}/api/v1/participants/balance-status"
    headers = {
        'Authorization': f'Bearer {token}',
        'Accept': 'application/json'
    }
    
    try:
        response = requests.get(url, headers=headers, timeout=10)
        response.raise_for_status()
        
        result = response.json()
        
        if result.get('data'):
            data = result['data']
            
            print(f"BIC: {data['bic']}")
            print(f"Participant: {data['participantName']}")
            print(f"Balance: {data['lastKnownBalance']} {data['currency']}")
            print(f"Zone: {data['currentZone']}")
            
            # Check zone status
            if data['currentZone'] == 'Red':
                print("⚠️ CRITICAL: Balance is in RED zone!")
            elif data['currentZone'] == 'Amber':
                print("⚠️ WARNING: Balance is in AMBER zone")
            else:
                print("✓ Balance is healthy (GREEN zone)")
            
            return data
        else:
            print(f"Error: {result.get('error')}")
            return None
            
    except requests.exceptions.RequestException as e:
        print(f"Request failed: {e}")
        return None

# Usage
api_host = "your-api-host.com"
token = "your-jwt-token-here"
balance_status = get_balance_status(api_host, token)
```

### C# (.NET)

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

public class BalanceStatusClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiHost;

    public BalanceStatusClient(string apiHost, string token)
    {
        _apiHost = apiHost;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<BalanceStatusResponse> GetBalanceStatusAsync()
    {
        try
        {
            var url = $"https://{_apiHost}/api/v1/participants/balance-status";
            var response = await _httpClient.GetAsync(url);
            
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiResponse>(content);
            
            if (result?.Data != null)
            {
                Console.WriteLine($"BIC: {result.Data.Bic}");
                Console.WriteLine($"Balance: {result.Data.LastKnownBalance:N2} {result.Data.Currency}");
                Console.WriteLine($"Zone: {result.Data.CurrentZone}");
                
                // Handle zone-specific logic
                switch (result.Data.CurrentZone)
                {
                    case "Red":
                        Console.WriteLine("⚠️ CRITICAL: Balance is in RED zone!");
                        break;
                    case "Amber":
                        Console.WriteLine("⚠️ WARNING: Balance is in AMBER zone");
                        break;
                    case "Green":
                        Console.WriteLine("✓ Balance is healthy (GREEN zone)");
                        break;
                }
                
                return result.Data;
            }
            
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
            throw;
        }
    }
}

// Data models
public class ApiResponse
{
    public BalanceStatusResponse Data { get; set; }
    public string Error { get; set; }
    public int HttpStatusCode { get; set; }
}

public class BalanceStatusResponse
{
    public string Bic { get; set; }
    public string ParticipantName { get; set; }
    public decimal? LastKnownBalance { get; set; }
    public string CurrentZone { get; set; }
    public DateTimeOffset? LastZoneChangeAt { get; set; }
    public DateTimeOffset? LastAlertSentAt { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public string Currency { get; set; }
}

// Usage
var client = new BalanceStatusClient("your-api-host.com", "your-jwt-token");
var status = await client.GetBalanceStatusAsync();
```

### Java (HttpClient)

```java
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import com.google.gson.Gson;
import com.google.gson.JsonObject;

public class BalanceStatusClient {
    private final HttpClient httpClient;
    private final String apiHost;
    private final String token;
    private final Gson gson;

    public BalanceStatusClient(String apiHost, String token) {
        this.apiHost = apiHost;
        this.token = token;
        this.httpClient = HttpClient.newHttpClient();
        this.gson = new Gson();
    }

    public BalanceStatus getBalanceStatus() throws Exception {
        String url = String.format("https://%s/api/v1/participants/balance-status", apiHost);
        
        HttpRequest request = HttpRequest.newBuilder()
            .uri(URI.create(url))
            .header("Authorization", "Bearer " + token)
            .header("Accept", "application/json")
            .GET()
            .build();

        HttpResponse<String> response = httpClient.send(
            request, 
            HttpResponse.BodyHandlers.ofString()
        );

        if (response.statusCode() == 200) {
            JsonObject jsonResponse = gson.fromJson(response.body(), JsonObject.class);
            JsonObject data = jsonResponse.getAsJsonObject("data");
            
            BalanceStatus status = gson.fromJson(data, BalanceStatus.class);
            
            System.out.println("BIC: " + status.getBic());
            System.out.println("Balance: " + status.getLastKnownBalance());
            System.out.println("Zone: " + status.getCurrentZone());
            
            // Handle zone-specific logic
            switch (status.getCurrentZone()) {
                case "Red":
                    System.out.println("⚠️ CRITICAL: Balance is in RED zone!");
                    break;
                case "Amber":
                    System.out.println("⚠️ WARNING: Balance is in AMBER zone");
                    break;
                case "Green":
                    System.out.println("✓ Balance is healthy (GREEN zone)");
                    break;
            }
            
            return status;
        } else {
            throw new Exception("Request failed with status: " + response.statusCode());
        }
    }
}

// Data model
class BalanceStatus {
    private String bic;
    private String participantName;
    private Double lastKnownBalance;
    private String currentZone;
    private String lastZoneChangeAt;
    private String lastAlertSentAt;
    private String lastUpdatedAt;
    private String currency;
    
    // Getters and setters...
}
```

---

## Best Practices

### 1. Polling Frequency
- **Recommended:** Poll every 5-10 minutes during business hours
- **Avoid:** Polling more frequently than every minute
- **Reason:** Balance updates occur when webhooks are received from the payment system

### 2. Caching
- Cache the response for at least 1 minute to reduce unnecessary API calls
- Invalidate cache when user explicitly requests a refresh

### 3. Error Handling
- Always implement retry logic with exponential backoff
- Log all errors for debugging purposes
- Display user-friendly error messages to end users

### 4. Token Management
- Store tokens securely (never in plain text or client-side storage)
- Implement token refresh logic before expiration
- Handle 401 errors by re-authenticating

### 5. UI/UX Recommendations
- Use color coding to indicate zone status:
  - 🟢 Green: Use green/success colors
  - 🟡 Amber: Use yellow/warning colors
  - 🔴 Red: Use red/danger colors
- Display balance with proper currency formatting
- Show last update timestamp to indicate data freshness
- Provide clear call-to-action buttons when in Amber or Red zones

### 6. Monitoring and Alerts
- Set up client-side monitoring to track API response times
- Alert your operations team when entering Amber or Red zones
- Monitor for consecutive API failures

### 7. Security
- Always use HTTPS for API calls
- Never log or expose JWT tokens in client-side code
- Implement proper CORS policies if calling from web applications
- Validate SSL certificates in production

---

## Troubleshooting

### Issue: "No threshold configuration found"
**Symptoms:** 404 error with message about missing threshold configuration

**Solutions:**
1. Verify your BIC is correctly configured in the system
2. Contact system administrator to set up threshold configuration
3. Ensure your JWT token contains the correct `tenantId` claim

### Issue: "Unauthorized" error
**Symptoms:** 401 error

**Solutions:**
1. Check if your token has expired
2. Verify the Authorization header format: `Bearer {token}`
3. Ensure you have the correct authentication credentials
4. Re-authenticate to obtain a new token

### Issue: Balance shows as null
**Symptoms:** `lastKnownBalance` is `null`

**Solutions:**
1. This is normal if no balance data has been received yet
2. Wait for the next balance webhook to be processed
3. Contact system administrator to verify webhook integration is working

### Issue: Zone not updating
**Symptoms:** Zone remains the same despite balance changes

**Solutions:**
1. Check `lastUpdatedAt` timestamp to verify data freshness
2. Verify balance webhooks are being received by the system
3. Contact technical support if the issue persists

### Issue: Slow API response
**Symptoms:** API takes more than 2 seconds to respond

**Solutions:**
1. Check your network connection
2. Verify API server status
3. Implement timeout handling (recommended: 10 seconds)
4. Contact technical support if consistently slow

---

## Support and Contact

For technical support or questions about this API:
- **Email:** support@your-organization.com
- **Documentation:** https://docs.your-organization.com
- **Status Page:** https://status.your-organization.com

---

## Changelog

### Version 1.0 (December 2025)
- Initial release of Participant Balance Status API
- Support for real-time balance and zone status retrieval
- JWT-based authentication
- Three-tier zone system (Green/Amber/Red)

---

## Appendix

### Sample Response Scenarios

#### Scenario 1: Healthy Balance (Green Zone)
```json
{
  "data": {
    "bic": "AALLSOS0",
    "participantName": "Amal Bank UAT",
    "lastKnownBalance": 85000.00,
    "currentZone": "Green",
    "lastZoneChangeAt": "2025-12-25T10:00:00.000Z",
    "lastAlertSentAt": null,
    "lastUpdatedAt": "2025-12-30T13:00:00.000Z",
    "currency": "USD"
  }
}
```

#### Scenario 2: Warning Zone (Amber)
```json
{
  "data": {
    "bic": "AALLSOS0",
    "participantName": "Amal Bank UAT",
    "lastKnownBalance": 45000.00,
    "currentZone": "Amber",
    "lastZoneChangeAt": "2025-12-30T11:30:00.000Z",
    "lastAlertSentAt": "2025-12-30T11:30:05.000Z",
    "lastUpdatedAt": "2025-12-30T13:00:00.000Z",
    "currency": "USD"
  }
}
```

#### Scenario 3: Critical Zone (Red)
```json
{
  "data": {
    "bic": "AALLSOS0",
    "participantName": "Amal Bank UAT",
    "lastKnownBalance": 15000.00,
    "currentZone": "Red",
    "lastZoneChangeAt": "2025-12-30T12:45:00.000Z",
    "lastAlertSentAt": "2025-12-30T12:45:05.000Z",
    "lastUpdatedAt": "2025-12-30T13:00:00.000Z",
    "currency": "USD"
  }
}
```

#### Scenario 4: No Balance Data Yet
```json
{
  "data": {
    "bic": "AALLSOS0",
    "participantName": "Amal Bank UAT",
    "lastKnownBalance": null,
    "currentZone": "Green",
    "lastZoneChangeAt": null,
    "lastAlertSentAt": null,
    "lastUpdatedAt": null,
    "currency": "USD"
  }
}
```

---

**Document Version:** 1.0  
**Last Updated:** December 30, 2025  
**API Version:** v1
