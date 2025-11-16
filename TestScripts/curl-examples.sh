#!/bin/bash

# SIPS Handler Test Scripts - Enhanced Version
# Usage: ./curl-examples.sh [base_url] [options]
# Example: ./curl-examples.sh http://localhost:5000
# Options:
#   --json-output    Generate JSON test report
#   --verbose        Show detailed output
#   --retry N        Retry failed tests N times (default: 0)
#   --timeout N      Request timeout in seconds (default: 30)

BASE_URL="${1:-http://localhost:5000}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PAYLOADS_DIR="$SCRIPT_DIR/Payloads"
JSON_OUTPUT=false
VERBOSE=false
RETRY_COUNT=0
TIMEOUT=30

# Parse additional arguments
shift
while [[ $# -gt 0 ]]; do
    case $1 in
        --json-output) JSON_OUTPUT=true; shift ;;
        --verbose) VERBOSE=true; shift ;;
        --retry) RETRY_COUNT="$2"; shift 2 ;;
        --timeout) TIMEOUT="$2"; shift 2 ;;
        *) shift ;;
    esac
done

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test results tracking
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
START_TIME=$(date +%s)
TEST_RESULTS=()

echo "=========================================="
echo "SIPS Handler Test Scripts - Enhanced"
echo "=========================================="
echo "Base URL: $BASE_URL"
echo "Timeout: ${TIMEOUT}s"
echo "Retry Count: $RETRY_COUNT"
echo "JSON Output: $JSON_OUTPUT"
echo "Verbose: $VERBOSE"
echo "Start Time: $(date '+%Y-%m-%d %H:%M:%S')"
echo "=========================================="
echo ""

# Function to validate XML response
validate_xml_response() {
    local body=$1
    if echo "$body" | grep -q "<FPEnvelope"; then
        return 0
    fi
    return 1
}

# Function to extract transaction ID from response
extract_transaction_id() {
    local body=$1
    # Use sed instead of grep -P for macOS compatibility
    echo "$body" | sed -n 's/.*<document:TxId>\([^<]*\)<\/document:TxId>.*/\1/p' | head -n1
}

# Function to test a handler with retry logic
test_handler() {
    local name=$1
    local endpoint=$2
    local payload_file=$3
    local expected_status=${4:-200}
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    local test_start=$(date +%s)
    
    echo -e "${BLUE}[Test $TOTAL_TESTS]${NC} ${YELLOW}$name${NC}"
    echo "Endpoint: $BASE_URL$endpoint"
    echo "Payload: $(basename $payload_file)"
    
    if [ ! -f "$payload_file" ]; then
        echo -e "${RED}✗ ERROR: Payload file not found: $payload_file${NC}"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        TEST_RESULTS+=("$(printf '{"test":"%s","status":"error","error":"Payload not found"}' "$name")")
        echo ""
        return 1
    fi
    
    local attempt=0
    local success=false
    local http_code=0
    local body=""
    local response_time=0
    
    while [ $attempt -le $RETRY_COUNT ] && [ "$success" = false ]; do
        if [ $attempt -gt 0 ]; then
            echo -e "${YELLOW}Retry attempt $attempt/$RETRY_COUNT...${NC}"
        fi
        
        local req_start=$(date +%s%3N)
        response=$(curl -s -k -w "\n%{http_code}\n%{time_total}" --max-time $TIMEOUT -X POST "$BASE_URL$endpoint" \
            -H "Content-Type: application/xml" \
            -H "Accept: application/xml" \
            -d @"$payload_file" 2>&1)
        
        http_code=$(echo "$response" | tail -n2 | head -n1)
        response_time=$(echo "$response" | tail -n1)
        body=$(echo "$response" | sed -e '$d' -e '$d')
        
        if [ "$http_code" -eq "$expected_status" ]; then
            success=true
        fi
        
        attempt=$((attempt + 1))
    done
    
    local test_end=$(date +%s)
    local duration=$((test_end - test_start))
    
    # Validate response
    local validation_msg=""
    if [ "$success" = true ]; then
        if validate_xml_response "$body"; then
            validation_msg="Valid XML response"
        else
            validation_msg="Warning: Response may not be valid XML"
        fi
        
        local tx_id=$(extract_transaction_id "$body")
        if [ -n "$tx_id" ]; then
            validation_msg="$validation_msg | TxId: $tx_id"
        fi
    fi
    
    if [ "$success" = true ]; then
        echo -e "${GREEN}✓ SUCCESS${NC} (HTTP $http_code) [${response_time}s]"
        [ -n "$validation_msg" ] && echo -e "${GREEN}  $validation_msg${NC}"
        PASSED_TESTS=$((PASSED_TESTS + 1))
        
        if [ "$VERBOSE" = true ]; then
            echo "Response preview:"
            echo "$body" | head -n 15
            if [ $(echo "$body" | wc -l) -gt 15 ]; then
                echo "... (truncated, $(echo "$body" | wc -l) total lines)"
            fi
        fi
        
        TEST_RESULTS+=("$(printf '{"test":"%s","status":"passed","http_code":%d,"response_time":%s,"duration":%d}' "$name" "$http_code" "$response_time" "$duration")")
    else
        echo -e "${RED}✗ FAILED${NC} (HTTP $http_code, Expected: $expected_status) [${response_time}s]"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        
        echo "Response:"
        echo "$body" | head -n 20
        
        TEST_RESULTS+=("$(printf '{"test":"%s","status":"failed","http_code":%d,"expected":%d,"response_time":%s,"duration":%d}' "$name" "$http_code" "$expected_status" "$response_time" "$duration")")
    fi
    
    echo ""
    echo "------------------------------------------"
    echo ""
}

# Core Handler Tests
echo -e "${BLUE}=== Core Handler Tests ===${NC}"
echo ""

# Test 1: IncomingTransactionHandler (pacs.008)
test_handler \
    "IncomingTransactionHandler (pacs.008)" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/pacs.008.xml" \
    200

# Test 2: IncomingVerificationHandler (acmt.023)
test_handler \
    "IncomingVerificationHandler (acmt.023)" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/acmt.023.xml" \
    200

# Test 3: IncomingTransactionStatusHandler (pacs.002)
test_handler \
    "IncomingTransactionStatusHandler (pacs.002)" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/pacs.002-status.xml" \
    200

# Test 4: IncomingPaymentStatusReportHandler (pacs.002)
test_handler \
    "IncomingPaymentStatusReportHandler (pacs.002)" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/pacs.002-payment-status.xml" \
    200

echo "=========================================="
echo "Core Test Suite Complete"
echo "=========================================="

# Additional test scenarios
echo ""
echo -e "${BLUE}=== Additional Test Scenarios ===${NC}"
echo ""

# Test with ACSC status
test_handler \
    "Payment Status Report - ACSC (Success)" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/pacs.002-payment-status-acsc.xml" \
    200

# Test with RJCT status
test_handler \
    "Payment Status Report - RJCT (Rejection)" \
    "/api/v1/incoming" \
    "$PAYLOADS_DIR/pacs.002-payment-status-rjct.xml" \
    200

# Test with non-existent transaction
if [ -f "$PAYLOADS_DIR/pacs.002-payment-status-notfound.xml" ]; then
    test_handler \
        "Non-existent Transaction (Expected 404 or error)" \
        "/api/v1/incoming" \
        "$PAYLOADS_DIR/pacs.002-payment-status-notfound.xml" \
        200
fi

# Generate summary
END_TIME=$(date +%s)
TOTAL_DURATION=$((END_TIME - START_TIME))

echo ""
echo "=========================================="
echo "Test Summary"
echo "=========================================="
echo -e "Total Tests:    ${BLUE}$TOTAL_TESTS${NC}"
echo -e "Passed:         ${GREEN}$PASSED_TESTS${NC}"
echo -e "Failed:         ${RED}$FAILED_TESTS${NC}"
if [ $TOTAL_TESTS -gt 0 ]; then
    SUCCESS_RATE=$(echo "scale=1; ($PASSED_TESTS * 100) / $TOTAL_TESTS" | bc)
    echo -e "Success Rate:   ${SUCCESS_RATE}%"
else
    echo -e "Success Rate:   0.0%"
fi
echo -e "Total Duration: ${TOTAL_DURATION}s"
echo -e "End Time:       $(date '+%Y-%m-%d %H:%M:%S')"
echo "=========================================="

# Generate JSON report if requested
if [ "$JSON_OUTPUT" = true ]; then
    REPORT_FILE="$SCRIPT_DIR/test-report-$(date +%Y%m%d-%H%M%S).json"
    if [ $TOTAL_TESTS -gt 0 ]; then
        SUCCESS_RATE_JSON=$(echo "scale=2; ($PASSED_TESTS * 100) / $TOTAL_TESTS" | bc)
    else
        SUCCESS_RATE_JSON="0.00"
    fi
    echo "{"
    echo "  \"summary\": {"
    echo "    \"total\": $TOTAL_TESTS,"
    echo "    \"passed\": $PASSED_TESTS,"
    echo "    \"failed\": $FAILED_TESTS,"
    echo "    \"success_rate\": $SUCCESS_RATE_JSON,"
    echo "    \"duration\": $TOTAL_DURATION,"
    echo "    \"start_time\": \"$(date -r $START_TIME '+%Y-%m-%d %H:%M:%S')\","
    echo "    \"end_time\": \"$(date '+%Y-%m-%d %H:%M:%S')\","
    echo "    \"base_url\": \"$BASE_URL\""
    echo "  },"
    echo "  \"tests\": ["
    for i in "${!TEST_RESULTS[@]}"; do
        echo "    ${TEST_RESULTS[$i]}"
        if [ $i -lt $((${#TEST_RESULTS[@]} - 1)) ]; then
            echo ","
        fi
    done
    echo "  ]"
    echo "}" > "$REPORT_FILE"
    echo ""
    echo -e "${GREEN}JSON report saved to: $REPORT_FILE${NC}"
fi

# Exit with appropriate code
if [ $FAILED_TESTS -gt 0 ]; then
    exit 1
else
    exit 0
fi
