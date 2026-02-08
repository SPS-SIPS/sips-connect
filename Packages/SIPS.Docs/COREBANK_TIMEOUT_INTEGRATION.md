# CoreBank Integration: Timeout Handling (2-Day Implementation)

## 🎯 Quick Summary

**Problem**: SIPS timeout → CoreBank auto-reverses → Double payment  
**Solution**: SIPS returns `PDNG` status → CoreBank waits → SIPS notifies final status  
**Timeline**: 1 day development + 1 day UAT = 2 days total

---

## 📋 What CoreBank Must Do (2 Changes Only)

### Change 1: Handle PDNG Status (30 minutes)

When you receive `PDNG` status from SIPS → **DO NOT REVERSE**

### Change 2: Add Completion Notification Endpoint (2 hours)

Receive final status from SIPS when resolved

---

## 🚀 Implementation Guide

### CHANGE 1: Handle PDNG Status

**Where**: In your existing payment response handler

**Before** (OLD CODE):

```csharp
var response = await _sipsClient.SendPayment(payment);

if (response.Status == "ACSC") {
    transaction.Status = "Success";
} else {
    // ❌ This causes double payment on timeout!
    transaction.Status = "Failed";
    await ReverseDebit(transaction);
}
```

**After** (NEW CODE):

```csharp
var response = await _sipsClient.SendPayment(payment);

if (response.Status == "ACSC") {
    transaction.Status = "Success";
}
else if (response.Status == "PDNG") {  // ✅ NEW: Handle pending
    transaction.Status = "Pending";
    // DO NOT REVERSE! Wait for completion notification
}
else if (response.Status == "RJCT") {
    transaction.Status = "Failed";
    await ReverseDebit(transaction);
}
else {  // ✅ NEW: Handle pending
    transaction.Status = "Pending";
}
```

**That's it!** ✅

---

### CHANGE 2: Add Completion Notification Endpoint

**Create new endpoint**: `POST /api/sips/completion-notification`

**IMPORTANT**: SIPS uses JSON field mapping via `jsonAdapter.json`. The actual field names you receive will be based on your configured mapping.

**Default Request Body** (if using standard mapping):

```json
{
  "originalTxId": "BANK20241123123456789",
  "originalEndToEndId": "E2E123456789",
  "status": "ACSC",
  "reason": "Payment successful",
  "additionalInfo": "Transaction resolved via SAF"
}
```

**Your Request Body** (based on your jsonAdapter.json mapping):

```json
{
  "txId": "BANK20241123123456789", // Your mapped field name
  "endToEndId": "E2E123456789", // Your mapped field name
  "txStatus": "ACSC", // Your mapped field name
  "statusReason": "Payment successful", // Your mapped field name
  "info": "Transaction resolved via SAF" // Your mapped field name
}
```

**Implementation** (Complete code):

```csharp
[HttpPost("completion-notification")]
public async Task<IActionResult> completion-notification(
    [FromBody] JsonObject notificationJson)
{
    // 1. Extract fields based on YOUR jsonAdapter.json mapping
    var txId = notificationJson["txId"]?.ToString();           // Use your mapped field name
    var status = notificationJson["txStatus"]?.ToString();     // Use your mapped field name

    if (string.IsNullOrEmpty(txId))
        return BadRequest("Missing transaction ID");

    // 2. Find the transaction
    var transaction = await _repository.GetByTxIdAsync(txId);
    if (transaction == null)
        return NotFound();

    // 3. Only update if currently Pending
    if (transaction.Status != "Pending")
        return Ok(); // Already processed

    // 4. Update based on final status
    if (status == "ACSC") {
        transaction.Status = "Success";
        // Keep the debit
    }
    else if (status == "RJCT") {
        transaction.Status = "Failed";
        await ReverseDebit(transaction); // NOW you can reverse
    }

    await _repository.SaveAsync(transaction);
    return Ok();
}
```

**Configure in SIPS** (`appsettings.json`):

```json
{
  "ISO20022Options": {
    "completion-notification": "https://corebank.yourdomain.com/api/sips/completion-notification"
  }
}
```

**Configure Field Mapping** (`jsonAdapter.json`):

Add this section to your existing jsonAdapter.json:

```json
{
  "Endpoints": {
    "CB_completion-notification": {
      "FieldMappings": [
        {
          "InternalField": "OriginalTxId",
          "UserField": "txId",
          "Type": "string"
        },
        {
          "InternalField": "OriginalEndToEndId",
          "UserField": "endToEndId",
          "Type": "string"
        },
        {
          "InternalField": "Status",
          "UserField": "txStatus",
          "Type": "string"
        },
        {
          "InternalField": "Reason",
          "UserField": "statusReason",
          "Type": "string"
        },
        {
          "InternalField": "AdditionalInfo",
          "UserField": "info",
          "Type": "string"
        }
      ]
    }
  }
}
```

**Note**: Adjust the `UserField` names to match your CoreBank's existing field naming conventions.

**That's it!** ✅

---

## 📊 Status Codes Reference

| Status     | Meaning | CoreBank Action        |
| ---------- | ------- | ---------------------- |
| `ACSC`     | Success | Keep debit ✅          |
| `PDNG`     | Pending | Wait, don't reverse ⏳ |
| `AnyError` | Pending | Wait, don't reverse ⏳ |
| `RJCT`     | Failed  | Reverse debit ❌       |

---

## 🧪 Testing Checklist (1 Day UAT)

### Test 1: Normal Success (15 minutes)

```
1. Send payment to SIPS
2. Receive ACSC immediately
3. ✅ Verify: Transaction marked as Success
```

### Test 2: Timeout → Success (30 minutes)

```
1. Send payment to SIPS
2. Receive PDNG (simulated timeout)
3. ✅ Verify: Transaction marked as Pending
4. ✅ Verify: NO reversal happened
5. Wait for completion notification (ACSC)
6. ✅ Verify: Transaction marked as Success
7. ✅ Verify: Debit still in place
```

### Test 3: Timeout → Failed (30 minutes)

```
1. Send payment to SIPS
2. Receive PDNG
3. ✅ Verify: Transaction marked as Pending
4. Wait for completion notification (RJCT)
5. ✅ Verify: Transaction marked as Failed
6. ✅ Verify: Reversal executed
```

### Test 4: Duplicate Notification (15 minutes)

```
1. Transaction already Success/Failed
2. Receive completion notification
3. ✅ Verify: No duplicate processing
```

---

## 📅 2-Day Timeline

### Day 1: Development (6-8 hours)

- **Hour 1**: Add PDNG handling to payment response (30 min)
- **Hour 2**: Create completion notification endpoint (2 hours)
- **Hour 3**: Coordinate with SIPS team on field mapping (30 min)
- **Hour 4**: Configure notification URL in SIPS (15 min)
- **Hour 5-6**: Unit tests (1 hour)
- **Hour 7-8**: Code review and deployment to UAT

### Day 2: UAT Testing (6-8 hours)

- **Morning**: Run all 4 test scenarios
- **Afternoon**: Fix any issues, retest
- **End of day**: Sign-off and deploy to production

---

## ⚠️ Critical Rules

### ✅ DO:

1. **Mark PDNG as Pending** - Don't treat it as failure
2. **Wait for notification** - Don't reverse pending transactions
3. **Update only Pending** - Ignore notifications for completed transactions

### ❌ DON'T:

1. **Don't reverse PDNG** - This causes double payment!
2. **Don't timeout waiting** - Notification will come eventually
3. **Don't poll SIPS** - Wait for the notification

---

## Configuration Required

### 1. SIPS Configuration (`appsettings.json`)

```json
{
  "ISO20022Options": {
    "completion-notification": "https://corebank.yourdomain.com/api/sips/completion-notification"
  }
}
```

### 2. JSON Field Mapping (`jsonAdapter.json`)

Add the `CB_completion-notification` endpoint mapping (see CHANGE 2 above) with your preferred field names.

**Example**:

- If CoreBank uses `txId` → Map `OriginalTxId` to `txId`
- If CoreBank uses `transactionId` → Map `OriginalTxId` to `transactionId`
- If CoreBank uses `status` → Map `Status` to `status`

### 3. Provide to SIPS Team

- Completion notification URL
- Your preferred field names (for jsonAdapter.json mapping)
- Expected response format (200 OK)

---

## Support During Implementation

**Questions?** Contact SIPS team:

- Technical lead: abdulshakur.aided@sps.so
- Integration support: abdulshakur.aided@sps.so

**Common Issues**:

1. **"How long until notification?"** - Usually 1-5 minutes, max 30 minutes
2. **"What if notification never comes?"** - Implement daily reconciliation (optional)
3. **"Can we poll instead?"** - No, wait for notification (simpler and more reliable)

---

## ✅ Success Criteria

After implementation, you should have:

- ✅ PDNG status handled without reversal
- ✅ Completion notification endpoint working
- ✅ All 4 test scenarios passing
- ✅ No double payments in UAT
- ✅ Production deployment ready

---

## 🎯 Summary: What Changed

**Before**: Timeout → FAIL → Auto-reverse → Double payment
**After**: Timeout → PDNG → Wait → Get final status → Correct action

**Your changes**: 2 code changes, 2 days total
**Result**: No more double payments! 🎉
