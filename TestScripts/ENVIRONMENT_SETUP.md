# Environment Setup Guide

This guide helps you configure your API project to work with the SIPS handler test scripts.

## Prerequisites

- .NET 8.0 SDK or later
- PostgreSQL database (or your configured database)
- Your API project
- Test certificates/keys for XML signatures

---

## Step 1: API Project Configuration

### 1.1 Register Handlers in DI Container

Ensure your handlers are registered in `Program.cs` or `Startup.cs`:

```csharp
// Add handler services
builder.Services.AddScoped<IIncomingTransactionHandler, IncomingTransactionHandler>();
builder.Services.AddScoped<IIncomingVerificationHandler, IncomingVerificationHandler>();
builder.Services.AddScoped<IIncomingTransactionStatusHandler, IncomingTransactionStatusHandler>();
builder.Services.AddScoped<IIncomingPaymentStatusReportHandler, IncomingPaymentStatusReportHandler>();

// Add dependencies
builder.Services.AddScoped<IPaymentRequestParser, PaymentRequestParser>();
builder.Services.AddScoped<IPaymentStatusReportParser, PaymentStatusReportParser>();
builder.Services.AddScoped<IPayeeVerificationRequestParser, PayeeVerificationRequestParser>();
builder.Services.AddScoped<IStatusOrchestrator, StatusOrchestrator>();
builder.Services.AddScoped<IISOMessageService, ISOMessageService>();
// ... other dependencies
```

### 1.2 Configure API Endpoints

Add controller endpoints or minimal API routes:

```csharp
// Option A: Using Controllers
[ApiController]
[Route("api/incoming")]
public class IncomingMessagesController : ControllerBase
{
    [HttpPost("transaction")]
    public async Task<IActionResult> ProcessTransaction(
        [FromBody] string xml,
        [FromServices] IIncomingTransactionHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(xml, ct);
        return Content(result, "application/xml");
    }

    [HttpPost("verification")]
    public async Task<IActionResult> ProcessVerification(
        [FromBody] string xml,
        [FromServices] IIncomingVerificationHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(xml, ct);
        return Content(result, "application/xml");
    }

    [HttpPost("status")]
    public async Task<IActionResult> ProcessStatus(
        [FromBody] string xml,
        [FromServices] IIncomingTransactionStatusHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(xml, ct);
        return Content(result, "application/xml");
    }

    [HttpPost("payment-status")]
    public async Task<IActionResult> ProcessPaymentStatus(
        [FromBody] string xml,
        [FromServices] IIncomingPaymentStatusReportHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(xml, ct);
        return Content(result, "application/xml");
    }
}

// Option B: Using Minimal APIs
app.MapPost("/api/incoming/transaction", async (
    [FromBody] string xml,
    IIncomingTransactionHandler handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(xml, ct);
    return Results.Content(result, "application/xml");
});

app.MapPost("/api/incoming/verification", async (
    [FromBody] string xml,
    IIncomingVerificationHandler handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(xml, ct);
    return Results.Content(result, "application/xml");
});

app.MapPost("/api/incoming/status", async (
    [FromBody] string xml,
    IIncomingTransactionStatusHandler handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(xml, ct);
    return Results.Content(result, "application/xml");
});

app.MapPost("/api/incoming/payment-status", async (
    [FromBody] string xml,
    IIncomingPaymentStatusReportHandler handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(xml, ct);
    return Results.Content(result, "application/xml");
});
```

### 1.3 Configure XML Input Formatter

Add XML input formatter to accept XML payloads:

```csharp
builder.Services.AddControllers()
    .AddXmlSerializerFormatters();

// Or for minimal APIs, add middleware to read raw XML
app.Use(async (context, next) =>
{
    if (context.Request.ContentType?.Contains("application/xml") == true)
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var xml = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;
        context.Items["RawXml"] = xml;
    }
    await next();
});
```

---

## Step 2: Database Setup

### 2.1 Create Database Schema

Ensure your database has the required tables:

```sql
-- ISO Messages table
CREATE TABLE IF NOT EXISTS iso_messages (
    Id SERIAL PRIMARY KEY,
    TxId VARCHAR(255) NOT NULL UNIQUE,
    EndToEndId VARCHAR(255),
    BizMsgIdr VARCHAR(255),
    MsgDefIdr VARCHAR(255),
    MsgId VARCHAR(255),
    Status VARCHAR(50) NOT NULL,
    Reason TEXT,
    AdditionalInfo TEXT,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP
);

-- ISO Message Statuses table
CREATE TABLE IF NOT EXISTS iso_message_statuses (
    Id SERIAL PRIMARY KEY,
    ISOMessageId INTEGER NOT NULL REFERENCES iso_messages(Id),
    Status VARCHAR(50) NOT NULL,
    Reason TEXT,
    AdditionalInfo TEXT,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    FOREIGN KEY (ISOMessageId) REFERENCES iso_messages(Id) ON DELETE CASCADE
);

-- Transactions table
CREATE TABLE IF NOT EXISTS transactions (
    Id SERIAL PRIMARY KEY,
    ISOMessageId INTEGER NOT NULL REFERENCES iso_messages(Id),
    Amount DECIMAL(18, 2) NOT NULL,
    Currency VARCHAR(3) NOT NULL,
    DebtorName VARCHAR(255),
    DebtorAccount VARCHAR(255),
    CreditorName VARCHAR(255),
    CreditorAccount VARCHAR(255),
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    FOREIGN KEY (ISOMessageId) REFERENCES iso_messages(Id) ON DELETE CASCADE
);

-- Indexes for performance
CREATE INDEX idx_iso_messages_txid ON iso_messages(TxId);
CREATE INDEX idx_iso_messages_status ON iso_messages(Status);
CREATE INDEX idx_iso_message_statuses_isomessageid ON iso_message_statuses(ISOMessageId);
```

### 2.2 Seed Test Data

Insert test transactions for status handler testing:

```sql
-- Insert test transaction for status testing
INSERT INTO iso_messages (TxId, EndToEndId, BizMsgIdr, MsgDefIdr, MsgId, Status, CreatedAt)
VALUES 
    ('TX-TEST-001', 'E2E-TEST-001', 'MSG-TEST-001', 'pacs.008.001.10', 'MSG-001', 'Pending', NOW()),
    ('TX-ACSC-001', 'E2E-ACSC-001', 'MSG-ACSC-001', 'pacs.008.001.10', 'MSG-ACSC', 'Pending', NOW()),
    ('TX-RJCT-001', 'E2E-RJCT-001', 'MSG-RJCT-001', 'pacs.008.001.10', 'MSG-RJCT', 'Pending', NOW());

-- Insert transaction details
INSERT INTO transactions (ISOMessageId, Amount, Currency, DebtorName, DebtorAccount, CreditorName, CreditorAccount)
SELECT 
    Id, 
    1000.00, 
    'USD', 
    'Test Debtor', 
    'ACC-DEBTOR-001', 
    'Test Creditor', 
    'ACC-CREDITOR-001'
FROM iso_messages 
WHERE TxId IN ('TX-TEST-001', 'TX-ACSC-001', 'TX-RJCT-001');
```

---

## Step 3: Configuration Settings

### 3.1 appsettings.json

Configure your application settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=sips_test;Username=postgres;Password=your_password"
  },
  "ISO20022Options": {
    "Transfer": "http://corebank-test/api/payment",
    "Return": "http://corebank-test/api/return",
    "Verification": "http://corebank-test/api/verification"
  },
  "CoreOptions": {
    "PublicKeyPath": "/path/to/public-key.pem",
    "PrivateKeyPath": "/path/to/private-key.pem"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SIPS": "Debug"
    }
  }
}
```

### 3.2 Environment Variables (Optional)

For sensitive data, use environment variables:

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Database=sips_test;..."
export CoreOptions__PrivateKeyPath="/secure/path/to/private-key.pem"
```

---

## Step 4: CoreBank Mock Setup (Optional)

For testing without a real CoreBank, set up a mock server:

### 4.1 Using JSON Server

```bash
npm install -g json-server

# Create db.json
cat > db.json << EOF
{
  "payments": [],
  "returns": [],
  "verifications": []
}
EOF

# Start mock server
json-server --watch db.json --port 3000
```

### 4.2 Using Minimal API Mock

```csharp
// Add to your API project for testing
app.MapPost("/corebank-mock/payment", () => 
    Results.Ok(new { Status = "ACSC", Reason = "", TxId = "MOCK-TX-001" }));

app.MapPost("/corebank-mock/return", () => 
    Results.Ok(new { Status = "ACSC", Reason = "" }));

app.MapPost("/corebank-mock/verification", () => 
    Results.Ok(new { IsVerified = true, Name = "Test User", Id = "TEST-ID" }));
```

---

## Step 5: Certificate Setup

### 5.1 Generate Test Certificates (for development)

```bash
# Generate private key
openssl genrsa -out private-key.pem 2048

# Generate public key
openssl rsa -in private-key.pem -pubout -out public-key.pem

# Generate self-signed certificate (optional)
openssl req -new -x509 -key private-key.pem -out certificate.pem -days 365
```

### 5.2 Configure Certificate Paths

Update your configuration to point to the certificates:

```json
{
  "CoreOptions": {
    "PublicKeyPath": "/path/to/public-key.pem",
    "PrivateKeyPath": "/path/to/private-key.pem",
    "CertificatePath": "/path/to/certificate.pem"
  }
}
```

---

## Step 6: Verify Setup

### 6.1 Start Your API

```bash
cd /path/to/your/api/project
dotnet run
```

### 6.2 Test Health Endpoint

```bash
curl http://localhost:5000/health
```

### 6.3 Test Database Connection

```bash
# Check if database is accessible
psql -h localhost -U postgres -d sips_test -c "SELECT COUNT(*) FROM iso_messages;"
```

### 6.4 Run First Test

```bash
cd /path/to/SIPS.Core.Tests/TestScripts
./curl-examples.sh http://localhost:5000
```

---

## Step 7: Troubleshooting

### Issue: Port Already in Use

```bash
# Find process using port 5000
lsof -i :5000

# Kill the process
kill -9 <PID>

# Or use a different port
dotnet run --urls "http://localhost:5001"
```

### Issue: Database Connection Failed

```bash
# Check PostgreSQL is running
sudo systemctl status postgresql

# Start PostgreSQL
sudo systemctl start postgresql

# Test connection
psql -h localhost -U postgres -c "SELECT version();"
```

### Issue: Certificate Not Found

```bash
# Verify file exists
ls -la /path/to/public-key.pem

# Check permissions
chmod 644 /path/to/public-key.pem
chmod 600 /path/to/private-key.pem
```

---

## Step 8: Production Considerations

### Security
- Use proper certificates (not self-signed)
- Store private keys securely (Azure Key Vault, AWS Secrets Manager)
- Enable HTTPS
- Implement rate limiting
- Add authentication/authorization

### Performance
- Enable response caching
- Configure connection pooling
- Add database indexes
- Monitor performance metrics

### Monitoring
- Set up application logging (Serilog, NLog)
- Configure health checks
- Add metrics collection (Prometheus, Application Insights)
- Set up alerts

---

## Quick Reference

### Start API
```bash
dotnet run --project /path/to/api
```

### Run Tests
```bash
cd TestScripts
./curl-examples.sh http://localhost:5000
```

### Check Logs
```bash
tail -f /path/to/logs/application.log
```

### Query Database
```sql
-- Recent transactions
SELECT * FROM iso_messages ORDER BY CreatedAt DESC LIMIT 10;

-- Status updates
SELECT * FROM iso_message_statuses ORDER BY CreatedAt DESC LIMIT 10;
```

---

**Your environment is ready when:**
- ✅ API starts without errors
- ✅ Database connection succeeds
- ✅ Test scripts run successfully
- ✅ Responses are properly signed
- ✅ Database shows expected changes

---

**Need help? See `QUICK_START.md` or `README.md` for more information.**
