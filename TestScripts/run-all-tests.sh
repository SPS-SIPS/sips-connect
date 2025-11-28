#!/bin/bash

# SIPS Connect - Automated Test Runner with Report Generation
# Usage: ./run-all-tests.sh [options]
# Options:
#   --url URL           API base URL (default: https://localhost:443)
#   --api-key KEY       API key for authentication
#   --api-secret SECRET API secret for authentication
#   --skip-auth         Skip authentication (for endpoints that don't require it)
#   --report-dir DIR    Report output directory (default: ./test-reports)
#   --format FORMAT     Report format: json, html, both (default: both)
#   --verbose           Show detailed output
#   --help              Show this help message

set -e

# Default values
BASE_URL="https://localhost:443"
API_KEY=""
API_SECRET=""
SKIP_AUTH=false
REPORT_DIR="./test-reports"
REPORT_FORMAT="both"
VERBOSE=false
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PAYLOADS_DIR="$SCRIPT_DIR/Payloads"
JSON_PAYLOADS_DIR="$SCRIPT_DIR/Payloads/JSON"

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --url) BASE_URL="$2"; shift 2 ;;
        --api-key) API_KEY="$2"; shift 2 ;;
        --api-secret) API_SECRET="$2"; shift 2 ;;
        --skip-auth) SKIP_AUTH=true; shift ;;
        --report-dir) REPORT_DIR="$2"; shift 2 ;;
        --format) REPORT_FORMAT="$2"; shift 2 ;;
        --verbose) VERBOSE=true; shift ;;
        --help)
            grep "^#" "$0" | grep -v "#!/bin/bash" | sed 's/^# //'
            exit 0
            ;;
        *) shift ;;
    esac
done

# Create report directory
mkdir -p "$REPORT_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
REPORT_FILE="$REPORT_DIR/test-report-$TIMESTAMP"

# Test tracking
declare -a TEST_RESULTS
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
SKIPPED_TESTS=0
START_TIME=$(date +%s)

# Print header
echo "=========================================="
echo "SIPS Connect - Automated Test Runner"
echo "=========================================="
echo "Base URL: $BASE_URL"
echo "Authentication: $([ "$SKIP_AUTH" = true ] && echo "Disabled" || echo "Enabled")"
echo "Report Directory: $REPORT_DIR"
echo "Report Format: $REPORT_FORMAT"
echo "Start Time: $(date '+%Y-%m-%d %H:%M:%S')"
echo "=========================================="
echo ""

# Function to run XML test
run_xml_test() {
    local name=$1
    local endpoint=$2
    local payload_file=$3
    local expected_status=${4:-200}
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    local test_start=$(date +%s.%N 2>/dev/null || date +%s)
    
    echo -e "${BLUE}[Test $TOTAL_TESTS]${NC} ${YELLOW}$name${NC}"
    
    if [ ! -f "$payload_file" ]; then
        echo -e "${RED}✗ SKIPPED - Payload not found${NC}"
        SKIPPED_TESTS=$((SKIPPED_TESTS + 1))
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"skipped\",\"reason\":\"Payload not found\"}")
        echo ""
        return
    fi
    
    # Build curl command
    local curl_cmd="curl -s -w '\n%{http_code}' -X POST '$BASE_URL$endpoint' \
        -H 'Content-Type: application/xml' \
        -H 'Accept: application/xml' \
        --max-time 30 \
        --insecure"
    
    # Add authentication if not skipped
    if [ "$SKIP_AUTH" = false ] && [ -n "$API_KEY" ]; then
        curl_cmd="$curl_cmd -H 'X-API-KEY: $API_KEY' -H 'X-API-SECRET: $API_SECRET'"
    fi
    
    curl_cmd="$curl_cmd -d @'$payload_file'"
    
    # Execute request
    local response=$(eval $curl_cmd 2>&1)
    local http_code=$(echo "$response" | tail -n1)
    local body=$(echo "$response" | sed '$d')
    
    local test_end=$(date +%s.%N 2>/dev/null || date +%s)
    local duration=$(echo "$test_end - $test_start" | bc 2>/dev/null || echo "0")
    
    # Check for connection errors
    if [ "$http_code" = "000" ] || [ -z "$http_code" ]; then
        echo -e "${RED}✗ FAILED - Connection Error${NC} [${duration}s]"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        
        if [ "$VERBOSE" = true ]; then
            echo "Error: Unable to connect to $BASE_URL$endpoint"
            echo "Response: $body"
        fi
        
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"failed\",\"http_code\":0,\"error\":\"Connection failed\",\"duration\":$duration}")
        echo ""
        return
    fi
    
    # Evaluate result
    if [ "$http_code" = "$expected_status" ]; then
        echo -e "${GREEN}✓ PASSED${NC} (HTTP $http_code) [${duration}s]"
        PASSED_TESTS=$((PASSED_TESTS + 1))
        
        # Extract transaction ID if present
        local tx_id=$(echo "$body" | sed -n 's/.*<document:TxId>\([^<]*\)<\/document:TxId>.*/\1/p' | head -n1)
        [ -n "$tx_id" ] && echo -e "  Transaction ID: $tx_id"
        
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"passed\",\"http_code\":$http_code,\"duration\":$duration,\"tx_id\":\"$tx_id\"}")
    else
        echo -e "${RED}✗ FAILED${NC} (HTTP $http_code, Expected: $expected_status) [${duration}s]"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        
        if [ "$VERBOSE" = true ]; then
            echo "Response:"
            echo "$body" | head -n 20
        fi
        
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"failed\",\"http_code\":$http_code,\"expected\":$expected_status,\"duration\":$duration}")
    fi
    
    echo ""
}

# Function to run JSON test
run_json_test() {
    local name=$1
    local endpoint=$2
    local payload_file=$3
    local expected_status=${4:-200}
    
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    local test_start=$(date +%s.%N 2>/dev/null || date +%s)
    
    echo -e "${BLUE}[Test $TOTAL_TESTS]${NC} ${YELLOW}$name${NC}"
    
    if [ ! -f "$payload_file" ]; then
        echo -e "${RED}✗ SKIPPED - Payload not found${NC}"
        SKIPPED_TESTS=$((SKIPPED_TESTS + 1))
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"skipped\",\"reason\":\"Payload not found\"}")
        echo ""
        return
    fi
    
    # Build curl command
    local curl_cmd="curl -s -w '\n%{http_code}' -X POST '$BASE_URL$endpoint' \
        -H 'Content-Type: application/json' \
        -H 'Accept: application/json' \
        --max-time 30 \
        --insecure"
    
    # Add authentication
    if [ "$SKIP_AUTH" = false ] && [ -n "$API_KEY" ]; then
        curl_cmd="$curl_cmd -H 'X-API-KEY: $API_KEY' -H 'X-API-SECRET: $API_SECRET'"
    fi
    
    curl_cmd="$curl_cmd -d @'$payload_file'"
    
    # Execute request
    local response=$(eval $curl_cmd 2>&1)
    local http_code=$(echo "$response" | tail -n1)
    local body=$(echo "$response" | sed '$d')
    
    local test_end=$(date +%s.%N 2>/dev/null || date +%s)
    local duration=$(echo "$test_end - $test_start" | bc 2>/dev/null || echo "0")
    
    # Check for connection errors
    if [ "$http_code" = "000" ] || [ -z "$http_code" ]; then
        echo -e "${RED}✗ FAILED - Connection Error${NC} [${duration}s]"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        
        if [ "$VERBOSE" = true ]; then
            echo "Error: Unable to connect to $BASE_URL$endpoint"
            echo "Response: $body"
        fi
        
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"failed\",\"http_code\":0,\"error\":\"Connection failed\",\"duration\":$duration}")
        echo ""
        return
    fi
    
    # Evaluate result
    if [ "$http_code" = "$expected_status" ]; then
        echo -e "${GREEN}✓ PASSED${NC} (HTTP $http_code) [${duration}s]"
        PASSED_TESTS=$((PASSED_TESTS + 1))
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"passed\",\"http_code\":$http_code,\"duration\":$duration}")
    else
        echo -e "${RED}✗ FAILED${NC} (HTTP $http_code, Expected: $expected_status) [${duration}s]"
        FAILED_TESTS=$((FAILED_TESTS + 1))
        
        if [ "$VERBOSE" = true ]; then
            echo "Response:"
            echo "$body" | head -n 20
        fi
        
        TEST_RESULTS+=("{\"test\":\"$name\",\"status\":\"failed\",\"http_code\":$http_code,\"expected\":$expected_status,\"duration\":$duration}")
    fi
    
    echo ""
}

# Run XML Tests
echo -e "${CYAN}=== XML Message Tests ===${NC}"
echo ""

run_xml_test "Payment Request (pacs.008)" "/api/v1/incoming" "$PAYLOADS_DIR/pacs.008.xml" 200
run_xml_test "Verification Request (acmt.023)" "/api/v1/incoming" "$PAYLOADS_DIR/acmt.023.xml" 200
run_xml_test "Transaction Status (pacs.002)" "/api/v1/incoming" "$PAYLOADS_DIR/pacs.002-status.xml" 200
run_xml_test "Payment Status - ACSC" "/api/v1/incoming" "$PAYLOADS_DIR/pacs.002-payment-status-acsc.xml" 200
run_xml_test "Payment Status - RJCT" "/api/v1/incoming" "$PAYLOADS_DIR/pacs.002-payment-status-rjct.xml" 200
run_xml_test "Return Request (pacs.004)" "/api/v1/incoming" "$PAYLOADS_DIR/pacs.004-return-request.xml" 200
run_xml_test "Status Request (pacs.028)" "/api/v1/incoming" "$PAYLOADS_DIR/pacs.028-status-request.xml" 200

# Run JSON Tests (Gateway APIs)
echo -e "${CYAN}=== Gateway API Tests (JSON) ===${NC}"
echo ""

run_json_test "Gateway - Verify Account" "/api/v1/gateway/Verify" "$JSON_PAYLOADS_DIR/gateway-verify-request.json" 200
run_json_test "Gateway - Payment Request" "/api/v1/gateway/Payment" "$JSON_PAYLOADS_DIR/gateway-payment-request.json" 200
run_json_test "Gateway - Status Request" "/api/v1/gateway/Status" "$JSON_PAYLOADS_DIR/gateway-status-request.json" 200
run_json_test "Gateway - Return Request" "/api/v1/gateway/Return" "$JSON_PAYLOADS_DIR/gateway-return-request.json" 200

# Run SomQR Tests
echo -e "${CYAN}=== SomQR API Tests (JSON) ===${NC}"
echo ""

run_json_test "SomQR - Generate Merchant QR" "/api/v1/somqr/GenerateMerchantQR" "$JSON_PAYLOADS_DIR/somqr-merchant-request.json" 200
run_json_test "SomQR - Generate Person QR" "/api/v1/somqr/GeneratePersonQR" "$JSON_PAYLOADS_DIR/somqr-person-request.json" 200

# Calculate summary
END_TIME=$(date +%s)
TOTAL_DURATION=$((END_TIME - START_TIME))
SUCCESS_RATE=0
if [ $TOTAL_TESTS -gt 0 ]; then
    SUCCESS_RATE=$(echo "scale=1; ($PASSED_TESTS * 100) / $TOTAL_TESTS" | bc)
fi

# Print summary
echo "=========================================="
echo "Test Summary"
echo "=========================================="
echo -e "Total Tests:    ${BLUE}$TOTAL_TESTS${NC}"
echo -e "Passed:         ${GREEN}$PASSED_TESTS${NC}"
echo -e "Failed:         ${RED}$FAILED_TESTS${NC}"
echo -e "Skipped:        ${YELLOW}$SKIPPED_TESTS${NC}"
echo -e "Success Rate:   ${SUCCESS_RATE}%"
echo "Total Duration: ${TOTAL_DURATION}s"
echo "End Time:       $(date '+%Y-%m-%d %H:%M:%S')"
echo "=========================================="

# Generate JSON report
if [ "$REPORT_FORMAT" = "json" ] || [ "$REPORT_FORMAT" = "both" ]; then
    JSON_REPORT="${REPORT_FILE}.json"
    
    cat > "$JSON_REPORT" <<EOF
{
  "summary": {
    "total": $TOTAL_TESTS,
    "passed": $PASSED_TESTS,
    "failed": $FAILED_TESTS,
    "skipped": $SKIPPED_TESTS,
    "success_rate": $SUCCESS_RATE,
    "duration": $TOTAL_DURATION,
    "start_time": "$(date -r $START_TIME '+%Y-%m-%d %H:%M:%S' 2>/dev/null || date '+%Y-%m-%d %H:%M:%S')",
    "end_time": "$(date '+%Y-%m-%d %H:%M:%S')",
    "base_url": "$BASE_URL",
    "authentication": $([ "$SKIP_AUTH" = true ] && echo "false" || echo "true")
  },
  "tests": [
    $(IFS=,; echo "${TEST_RESULTS[*]}")
  ]
}
EOF
    
    echo ""
    echo -e "${GREEN}✓ JSON report saved:${NC} $JSON_REPORT"
fi

# Generate HTML report
if [ "$REPORT_FORMAT" = "html" ] || [ "$REPORT_FORMAT" = "both" ]; then
    HTML_REPORT="${REPORT_FILE}.html"
    
    cat > "$HTML_REPORT" <<'EOF'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>SIPS Connect - Test Report</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif; background: #f5f5f5; padding: 20px; }
        .container { max-width: 1200px; margin: 0 auto; background: white; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; border-radius: 8px 8px 0 0; }
        .header h1 { font-size: 28px; margin-bottom: 10px; }
        .header p { opacity: 0.9; }
        .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; padding: 30px; border-bottom: 1px solid #eee; }
        .summary-card { text-align: center; padding: 20px; background: #f8f9fa; border-radius: 8px; }
        .summary-card .value { font-size: 36px; font-weight: bold; margin: 10px 0; }
        .summary-card .label { color: #666; font-size: 14px; text-transform: uppercase; letter-spacing: 1px; }
        .summary-card.passed .value { color: #28a745; }
        .summary-card.failed .value { color: #dc3545; }
        .summary-card.skipped .value { color: #ffc107; }
        .summary-card.total .value { color: #007bff; }
        .tests { padding: 30px; }
        .test-section { margin-bottom: 30px; }
        .test-section h2 { font-size: 20px; margin-bottom: 15px; color: #333; border-bottom: 2px solid #667eea; padding-bottom: 10px; }
        .test-item { display: flex; align-items: center; padding: 15px; margin-bottom: 10px; border-radius: 6px; border-left: 4px solid #ddd; background: #f8f9fa; }
        .test-item.passed { border-left-color: #28a745; background: #d4edda; }
        .test-item.failed { border-left-color: #dc3545; background: #f8d7da; }
        .test-item.skipped { border-left-color: #ffc107; background: #fff3cd; }
        .test-status { width: 80px; font-weight: bold; text-transform: uppercase; font-size: 12px; }
        .test-status.passed { color: #28a745; }
        .test-status.failed { color: #dc3545; }
        .test-status.skipped { color: #ffc107; }
        .test-name { flex: 1; font-weight: 500; }
        .test-details { display: flex; gap: 20px; font-size: 14px; color: #666; }
        .footer { padding: 20px 30px; background: #f8f9fa; border-radius: 0 0 8px 8px; text-align: center; color: #666; font-size: 14px; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🚀 SIPS Connect - Test Report</h1>
            <p>Automated Test Execution Report</p>
        </div>
        
        <div class="summary">
            <div class="summary-card total">
                <div class="label">Total Tests</div>
                <div class="value" id="total-tests">0</div>
            </div>
            <div class="summary-card passed">
                <div class="label">Passed</div>
                <div class="value" id="passed-tests">0</div>
            </div>
            <div class="summary-card failed">
                <div class="label">Failed</div>
                <div class="value" id="failed-tests">0</div>
            </div>
            <div class="summary-card skipped">
                <div class="label">Skipped</div>
                <div class="value" id="skipped-tests">0</div>
            </div>
            <div class="summary-card">
                <div class="label">Success Rate</div>
                <div class="value" id="success-rate">0%</div>
            </div>
            <div class="summary-card">
                <div class="label">Duration</div>
                <div class="value" id="duration">0s</div>
            </div>
        </div>
        
        <div class="tests" id="test-results">
            <!-- Tests will be inserted here by JavaScript -->
        </div>
        
        <div class="footer">
            <p>Generated on <span id="timestamp"></span> | Base URL: <span id="base-url"></span></p>
        </div>
    </div>
    
    <script>
        // Load test data from JSON
        fetch('test-report-TIMESTAMP.json')
            .then(response => response.json())
            .then(data => {
                // Update summary
                document.getElementById('total-tests').textContent = data.summary.total;
                document.getElementById('passed-tests').textContent = data.summary.passed;
                document.getElementById('failed-tests').textContent = data.summary.failed;
                document.getElementById('skipped-tests').textContent = data.summary.skipped;
                document.getElementById('success-rate').textContent = data.summary.success_rate + '%';
                document.getElementById('duration').textContent = data.summary.duration + 's';
                document.getElementById('timestamp').textContent = data.summary.end_time;
                document.getElementById('base-url').textContent = data.summary.base_url;
                
                // Group tests by category
                const xmlTests = data.tests.filter(t => t.test.includes('pacs') || t.test.includes('acmt'));
                const gatewayTests = data.tests.filter(t => t.test.includes('Gateway'));
                const somqrTests = data.tests.filter(t => t.test.includes('SomQR'));
                
                // Render tests
                const container = document.getElementById('test-results');
                
                if (xmlTests.length > 0) {
                    container.innerHTML += renderTestSection('XML Message Tests', xmlTests);
                }
                if (gatewayTests.length > 0) {
                    container.innerHTML += renderTestSection('Gateway API Tests', gatewayTests);
                }
                if (somqrTests.length > 0) {
                    container.innerHTML += renderTestSection('SomQR API Tests', somqrTests);
                }
            });
        
        function renderTestSection(title, tests) {
            let html = `<div class="test-section"><h2>${title}</h2>`;
            tests.forEach(test => {
                html += `
                    <div class="test-item ${test.status}">
                        <div class="test-status ${test.status}">${test.status}</div>
                        <div class="test-name">${test.test}</div>
                        <div class="test-details">
                            ${test.http_code ? `<span>HTTP ${test.http_code}</span>` : ''}
                            ${test.duration ? `<span>${test.duration}s</span>` : ''}
                            ${test.tx_id ? `<span>TxID: ${test.tx_id}</span>` : ''}
                        </div>
                    </div>
                `;
            });
            html += '</div>';
            return html;
        }
    </script>
</body>
</html>
EOF
    
    # Replace TIMESTAMP placeholder
    sed -i.bak "s/TIMESTAMP/$TIMESTAMP/g" "$HTML_REPORT" && rm "${HTML_REPORT}.bak"
    
    echo -e "${GREEN}✓ HTML report saved:${NC} $HTML_REPORT"
fi

echo ""
echo "Reports saved to: $REPORT_DIR"
echo ""

# Exit with appropriate code
if [ $FAILED_TESTS -gt 0 ]; then
    exit 1
else
    exit 0
fi
