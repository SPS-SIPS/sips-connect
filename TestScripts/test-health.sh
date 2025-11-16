#!/bin/bash

# Health Check Test Script
# Usage: ./test-health.sh [base_url]

BASE_URL="${1:-https://localhost:443}"

echo "=========================================="
echo "SIPS Connect Health Check Test"
echo "=========================================="
echo "Testing: $BASE_URL/health"
echo ""

# Test the health endpoint
echo "Sending GET request to /health..."
response=$(curl -s -k -w "\n%{http_code}" "$BASE_URL/health" 2>&1)

http_code=$(echo "$response" | tail -n1)
body=$(echo "$response" | sed '$d')

echo ""
echo "HTTP Status Code: $http_code"
echo ""
echo "Response Body:"
echo "$body" | jq '.' 2>/dev/null || echo "$body"
echo ""

# Check status
if [ "$http_code" = "200" ]; then
    echo "✅ Health check passed - All systems operational"
    exit 0
elif [ "$http_code" = "503" ]; then
    echo "⚠️  Health check returned degraded status"
    exit 1
else
    echo "❌ Health check failed with unexpected status code"
    exit 1
fi
