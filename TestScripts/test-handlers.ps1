# SIPS Handler Test Scripts (PowerShell) - Enhanced Version
# Usage: .\test-handlers.ps1 -BaseUrl "http://localhost:5000" [options]
# Example: .\test-handlers.ps1 -BaseUrl "http://localhost:5000" -JsonOutput -Verbose
#
# Parameters:
#   -BaseUrl        API base URL (default: http://localhost:5000)
#   -JsonOutput     Generate JSON test report
#   -VerboseOutput  Show detailed output
#   -RetryCount     Number of retries for failed tests (default: 0)
#   -Timeout        Request timeout in seconds (default: 30)

param(
    [string]$BaseUrl = "http://localhost:5000",
    [switch]$JsonOutput = $false,
    [switch]$VerboseOutput = $false,
    [int]$RetryCount = 0,
    [int]$Timeout = 30
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PayloadsDir = Join-Path $ScriptDir "Payloads"

# Test results tracking
$script:TotalTests = 0
$script:PassedTests = 0
$script:FailedTests = 0
$script:StartTime = Get-Date
$script:TestResults = @()

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "SIPS Handler Test Scripts - Enhanced" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Base URL: $BaseUrl" -ForegroundColor Cyan
Write-Host "Timeout: ${Timeout}s" -ForegroundColor Cyan
Write-Host "Retry Count: $RetryCount" -ForegroundColor Cyan
Write-Host "JSON Output: $JsonOutput" -ForegroundColor Cyan
Write-Host "Verbose: $VerboseOutput" -ForegroundColor Cyan
Write-Host "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Function to validate XML response
function Test-XmlResponse {
    param([string]$Body)
    return $Body -match "<FPEnvelope"
}

# Function to extract transaction ID from response
function Get-TransactionId {
    param([string]$Body)
    if ($Body -match '<document:TxId>([^<]+)</document:TxId>') {
        return $matches[1]
    }
    return $null
}

# Function to test a handler with retry logic
function Test-Handler {
    param(
        [string]$Name,
        [string]$Endpoint,
        [string]$PayloadFile,
        [int]$ExpectedStatus = 200
    )
    
    $script:TotalTests++
    $testStart = Get-Date
    
    Write-Host "[Test $($script:TotalTests)] " -ForegroundColor Blue -NoNewline
    Write-Host "$Name" -ForegroundColor Yellow
    Write-Host "Endpoint: $BaseUrl$Endpoint"
    Write-Host "Payload: $(Split-Path $PayloadFile -Leaf)"
    
    if (-not (Test-Path $PayloadFile)) {
        Write-Host "✗ ERROR: Payload file not found: $PayloadFile" -ForegroundColor Red
        $script:FailedTests++
        $script:TestResults += @{
            test = $Name
            status = "error"
            error = "Payload not found"
        }
        Write-Host ""
        return
    }
    
    $attempt = 0
    $success = $false
    $httpCode = 0
    $responseBody = ""
    $responseTime = 0
    
    while ($attempt -le $RetryCount -and -not $success) {
        if ($attempt -gt 0) {
            Write-Host "Retry attempt $attempt/$RetryCount..." -ForegroundColor Yellow
        }
        
        try {
            $body = Get-Content $PayloadFile -Raw
            $headers = @{
                "Content-Type" = "application/xml"
                "Accept" = "application/xml"
            }
            
            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-WebRequest -Uri "$BaseUrl$Endpoint" `
                -Method POST `
                -Headers $headers `
                -Body $body `
                -TimeoutSec $Timeout `
                -UseBasicParsing `
                -SkipCertificateCheck `
                -ErrorAction Stop
            $stopwatch.Stop()
            
            $httpCode = $response.StatusCode
            $responseBody = $response.Content
            $responseTime = $stopwatch.Elapsed.TotalSeconds
            
            if ($httpCode -eq $ExpectedStatus) {
                $success = $true
            }
        }
        catch {
            $stopwatch.Stop()
            $responseTime = $stopwatch.Elapsed.TotalSeconds
            
            if ($_.Exception.Response) {
                $httpCode = $_.Exception.Response.StatusCode.value__
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $responseBody = $reader.ReadToEnd()
                $reader.Close()
                $stream.Close()
                
                if ($httpCode -eq $ExpectedStatus) {
                    $success = $true
                }
            } else {
                $responseBody = $_.Exception.Message
            }
        }
        
        $attempt++
    }
    
    $testEnd = Get-Date
    $duration = ($testEnd - $testStart).TotalSeconds
    
    # Validate response
    $validationMsg = ""
    if ($success) {
        if (Test-XmlResponse $responseBody) {
            $validationMsg = "Valid XML response"
        } else {
            $validationMsg = "Warning: Response may not be valid XML"
        }
        
        $txId = Get-TransactionId $responseBody
        if ($txId) {
            $validationMsg += " | TxId: $txId"
        }
    }
    
    if ($success) {
        Write-Host "✓ SUCCESS " -ForegroundColor Green -NoNewline
        Write-Host "(HTTP $httpCode) [" -NoNewline
        Write-Host ("{0:N2}s" -f $responseTime) -NoNewline
        Write-Host "]"
        
        if ($validationMsg) {
            Write-Host "  $validationMsg" -ForegroundColor Green
        }
        
        $script:PassedTests++
        
        if ($VerboseOutput) {
            Write-Host "Response preview:"
            $responseLines = $responseBody -split "`n"
            $preview = $responseLines | Select-Object -First 15
            $preview | ForEach-Object { Write-Host $_ }
            if ($responseLines.Count -gt 15) {
                Write-Host "... (truncated, $($responseLines.Count) total lines)"
            }
        }
        
        $script:TestResults += @{
            test = $Name
            status = "passed"
            http_code = $httpCode
            response_time = [math]::Round($responseTime, 2)
            duration = [math]::Round($duration, 2)
        }
    } else {
        Write-Host "✗ FAILED " -ForegroundColor Red -NoNewline
        Write-Host "(HTTP $httpCode, Expected: $ExpectedStatus) [" -NoNewline
        Write-Host ("{0:N2}s" -f $responseTime) -NoNewline
        Write-Host "]"
        
        $script:FailedTests++
        
        Write-Host "Response:"
        $responseLines = $responseBody -split "`n"
        $responseLines | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
        
        $script:TestResults += @{
            test = $Name
            status = "failed"
            http_code = $httpCode
            expected = $ExpectedStatus
            response_time = [math]::Round($responseTime, 2)
            duration = [math]::Round($duration, 2)
        }
    }
    
    Write-Host ""
    Write-Host "------------------------------------------"
    Write-Host ""
}

# Core Handler Tests
Write-Host "=== Core Handler Tests ===" -ForegroundColor Blue
Write-Host ""

# Test 1: IncomingTransactionHandler (pacs.008)
Test-Handler `
    -Name "IncomingTransactionHandler (pacs.008)" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "pacs.008.xml") `
    -ExpectedStatus 200

# Test 2: IncomingVerificationHandler (acmt.023)
Test-Handler `
    -Name "IncomingVerificationHandler (acmt.023)" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "acmt.023.xml") `
    -ExpectedStatus 200

# Test 3: IncomingTransactionStatusHandler (pacs.002)
Test-Handler `
    -Name "IncomingTransactionStatusHandler (pacs.002)" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "pacs.002-status.xml") `
    -ExpectedStatus 200

# Test 4: IncomingPaymentStatusReportHandler (pacs.002)
Test-Handler `
    -Name "IncomingPaymentStatusReportHandler (pacs.002)" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "pacs.002-payment-status.xml") `
    -ExpectedStatus 200

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Core Test Suite Complete" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# Additional test scenarios
Write-Host ""
Write-Host "=== Additional Test Scenarios ===" -ForegroundColor Blue
Write-Host ""

# Test with ACSC status
Test-Handler `
    -Name "Payment Status Report - ACSC (Success)" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "pacs.002-payment-status-acsc.xml") `
    -ExpectedStatus 200

# Test with RJCT status
Test-Handler `
    -Name "Payment Status Report - RJCT (Rejection)" `
    -Endpoint "/api/v1/incoming" `
    -PayloadFile (Join-Path $PayloadsDir "pacs.002-payment-status-rjct.xml") `
    -ExpectedStatus 200

# Test with non-existent transaction
if (Test-Path (Join-Path $PayloadsDir "pacs.002-payment-status-notfound.xml")) {
    Test-Handler `
        -Name "Non-existent Transaction (Expected 404 or error)" `
        -Endpoint "/api/v1/incoming" `
        -PayloadFile (Join-Path $PayloadsDir "pacs.002-payment-status-notfound.xml") `
        -ExpectedStatus 200
}

# Generate summary
$endTime = Get-Date
$totalDuration = ($endTime - $script:StartTime).TotalSeconds
$successRate = if ($script:TotalTests -gt 0) { ($script:PassedTests / $script:TotalTests) * 100 } else { 0 }

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Total Tests:    " -NoNewline
Write-Host $script:TotalTests -ForegroundColor Blue
Write-Host "Passed:         " -NoNewline
Write-Host $script:PassedTests -ForegroundColor Green
Write-Host "Failed:         " -NoNewline
Write-Host $script:FailedTests -ForegroundColor Red
Write-Host "Success Rate:   $($successRate.ToString('F1'))%"
Write-Host "Total Duration: $($totalDuration.ToString('F2'))s"
Write-Host "End Time:       $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "=========================================" -ForegroundColor Cyan

# Generate JSON report if requested
if ($JsonOutput) {
    $reportFile = Join-Path $ScriptDir "test-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    
    $report = @{
        summary = @{
            total = $script:TotalTests
            passed = $script:PassedTests
            failed = $script:FailedTests
            success_rate = [math]::Round($successRate, 2)
            duration = [math]::Round($totalDuration, 2)
            start_time = $script:StartTime.ToString('yyyy-MM-dd HH:mm:ss')
            end_time = $endTime.ToString('yyyy-MM-dd HH:mm:ss')
            base_url = $BaseUrl
        }
        tests = $script:TestResults
    }
    
    $report | ConvertTo-Json -Depth 10 | Out-File -FilePath $reportFile -Encoding UTF8
    Write-Host ""
    Write-Host "JSON report saved to: $reportFile" -ForegroundColor Green
}

# Exit with appropriate code
if ($script:FailedTests -gt 0) {
    exit 1
} else {
    exit 0
}
