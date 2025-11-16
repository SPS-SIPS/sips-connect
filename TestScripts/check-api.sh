#!/bin/bash

# API Health Check Script
# Usage: ./check-api.sh [base_url]

BASE_URL="${1:-http://localhost:5000}"

echo "=========================================="
echo "SIPS API Health Check"
echo "=========================================="
echo "Checking: $BASE_URL"
echo ""

# Check if API is reachable
echo "Testing connection..."
HTTP_CODE=$(curl -s -k -o /dev/null -w "%{http_code}" --max-time 5 "$BASE_URL/api/v1/incoming" -X POST -H "Content-Type: application/xml" 2>/dev/null)

if [ -z "$HTTP_CODE" ] || [ "$HTTP_CODE" = "000" ]; then
    echo "❌ API is NOT reachable at $BASE_URL"
    echo ""
    echo "Troubleshooting:"
    echo "1. Check if the API is running:"
    echo "   cd /Users/maven/source/SIPS/SIPS.Connect"
    echo "   dotnet run"
    echo ""
    echo "2. Check which ports are in use:"
    echo "   lsof -i :5000"
    echo "   lsof -i :8080"
    echo ""
    echo "3. Start the API using watch.sh:"
    echo "   cd /Users/maven/source/SIPS/SIPS.Connect"
    echo "   ./watch.sh"
    echo ""
    echo "4. Common URLs to try:"
    echo "   - https://localhost:443 (default via watch.sh)"
    echo "   - http://localhost:5000"
    echo "   - https://localhost:5001"
    exit 1
else
    echo "✅ API is reachable!"
    echo "   HTTP Status: $HTTP_CODE"
    
    if [ "$HTTP_CODE" = "200" ]; then
        echo "   Status: OK - Ready for testing"
    elif [ "$HTTP_CODE" = "403" ]; then
        echo "   Status: Forbidden - May need authentication"
    elif [ "$HTTP_CODE" = "401" ]; then
        echo "   Status: Unauthorized - Authentication required"
    elif [ "$HTTP_CODE" = "404" ]; then
        echo "   Status: Not Found - Check endpoint path"
    else
        echo "   Status: Unexpected response"
    fi
    
    echo ""
    echo "You can now run the tests:"
    echo "  ./curl-examples.sh $BASE_URL"
    exit 0
fi
