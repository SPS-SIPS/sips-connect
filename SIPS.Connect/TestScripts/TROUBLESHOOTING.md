# Troubleshooting Guide

## Issue: HTTP 000 - Connection Failed

### Symptoms

```
✗ FAILED (HTTP 000, Expected: 200)
Response:
000
```

### Cause

The API is not running or not reachable at the specified URL.

### Solution

#### 1. Check API Status

```bash
./check-api.sh
# or with custom URL
./check-api.sh https://localhost:443
```

#### 2. Start the API

```bash
cd /path/to/your/api/project
dotnet run
# or
dotnet watch
```

**Note:** The default scripts assume `http://localhost:5000`. Adjust the URL parameter if your API uses a different port.

#### 3. Find Running Ports

```bash
# Check port 5000
lsof -i :5000

# Check port 8080
lsof -i :8080
```

#### 4. Test Correct URL

Make sure the URL matches where your API is actually running.

```bash
# Default (http://localhost:5000)
./curl-examples.sh

# Custom URL
./curl-examples.sh https://localhost:443

# Wrong - using 0.0.0.0
./curl-examples.sh https://0.0.0.0:8080
```

## Issue: AWK Syntax Error (Fixed)

### Symptoms

```
awk: syntax error at source line 1
Success Rate:    Success Rate:
```

### Cause

macOS uses BSD awk which has different syntax than GNU awk.

### Solution

✅ **Already fixed!** The script now uses `bc` instead of `awk` for calculations.

## Issue: HTTP 403 - Forbidden

### Symptoms

```
HTTP Status: 403
Status: Forbidden
```

### Cause

The API endpoint may require authentication or the request is missing required headers.

### Solution

#### Check if authentication is required

Look at the API configuration in `Program.cs` or `Startup.cs`:

```csharp
// If you see this, authentication is required
[Authorize(Roles = Gateway)]
```

#### For the `/api/v1/incoming` endpoint

This endpoint should NOT require authentication for incoming ISO 20022 messages. If you're getting 403, check:

1. **CORS settings** - May be blocking the request
2. **Request headers** - Ensure Content-Type is set correctly
3. **API logs** - Check for detailed error messages

## Issue: HTTP 401 - Unauthorized

### Symptoms

```
HTTP Status: 401
```

### Cause

Authentication required but not provided.

### Solution

Add authentication headers to the test script or disable authentication for testing.

## Issue: HTTP 404 - Not Found

### Symptoms

```
HTTP Status: 404
```

### Cause

The endpoint path is incorrect.

### Solution

Verify the endpoint:

- ✅ Correct: `/api/v1/incoming`

## Quick Reference

### Check API Health

```bash
./check-api.sh
# or explicitly
./check-api.sh https://localhost:443
```

### Run Tests (Basic)

```bash
./curl-examples.sh
# or explicitly
./curl-examples.sh https://localhost:443
```

### Run Tests (Verbose for debugging)

```bash
./curl-examples.sh https://localhost:443 --verbose
```

### Run Tests (With retry)

```bash
./curl-examples.sh https://localhost:443 --retry 3
```

### Test Single Endpoint

```bash
curl -X POST https://localhost:443/api/v1/incoming \
  -H "Content-Type: application/xml" \
  -d @Payloads/pacs.008.xml
```

## Common Port Configurations

| Environment               | URL                     | How to Start                         |
| ------------------------- | ----------------------- | ------------------------------------ |
| Local Development (HTTP)  | http://localhost:5000   | `dotnet run`                         |
| Local Development (HTTPS) | https://localhost:5001  | `dotnet run` (with HTTPS configured) |
| Docker                    | http://localhost:8080   | `docker-compose up`                  |
| Production                | https://api.example.com | Deployment-specific                  |

## Getting Help

1. **Check API logs** - Look for error messages in your API console
2. **Use verbose mode** - `./curl-examples.sh http://localhost:5000 --verbose`
3. **Check health endpoint** - `./check-api.sh`
4. **Verify API is running** - `lsof -i :5000` (or your port)
5. **Check test configuration** - Review `test-config.json`

## Next Steps

After fixing connection issues:

1. ✅ Start API: `cd /path/to/your/api && dotnet run`
2. ✅ Verify API is running: `./check-api.sh`
3. ✅ Run tests: `./curl-examples.sh`
4. ✅ Check results and fix any failures
5. ✅ Review API logs for detailed error messages
