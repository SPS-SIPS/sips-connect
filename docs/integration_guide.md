# SIPS Connect: QR Integration Guide

This guide provides examples for integrating the SIPS Connect QR verification and payment flow into your application.

## Flow Overview

1. **Verify QR**: Call the `Verify` endpoint with the raw QR string.
2. **Handle Response**: 
    - Parse the `parsed` object from the response.
    - Check `pointOfInitializationMethod` to determine if the QR is **Static (11)** or **Dynamic (12)**.
    - If Static, allow the user to input the amount and particulars.
3. **Map Transaction Fields**:
    - `lclInstrument`: Always `"CRTRM"`.
    - `ctgPurp`: Based on `payloadFormatIndicator` and `pointOfInitializationMethod`.
4. **Confirm Payment**: Call the `Payment` endpoint to finalize the transaction.

---

## 1. Verification and State Preparation

### Category Purpose (`ctgPurp`) Logic
| QR Type | Method | Category Purpose |
| :--- | :--- | :--- |
| P2P (02) | Any | `C2CCRT` |
| P2M (01) | Static (11) | `C2BSQR` |
| P2M (01) | Dynamic (12) | `C2BDQR` |

---

## 2. Implementation Examples

### TypeScript (Web / Node)

```typescript
interface QrResponse {
  isVerified: boolean;
  parsed: {
    accountId: string;
    bankName: string;
    bankBICCode: string;
    amount?: number;
    particulars?: string;
    payloadFormatIndicator: string;
    pointOfInitializationMethod: string;
  };
}

async function handleFlow(qrCode: string) {
  // Step 1: Verify
  const verifyRes = await fetch('/api/v1/Gateway/Verify', {
    method: 'POST',
    body: JSON.stringify({ QRCode: qrCode })
  });
  const data: QrResponse = await verifyRes.json();

  if (data.isVerified) {
    const isStatic = data.parsed.pointOfInitializationMethod === "11";
    
    // Step 2: Prepare DTO
    let ctgPurp = "C2CCRT";
    if (data.parsed.payloadFormatIndicator === "01") {
      ctgPurp = isStatic ? "C2BSQR" : "C2BDQR";
    }

    const paymentRequest = {
      agent: data.parsed.bankBICCode,
      lclInstrument: "CRTRM",
      ctgPurp: ctgPurp,
      amount: data.parsed.amount || 0, // Allow user input if 0
      crAccount: data.parsed.accountId,
      narration: data.parsed.particulars || ""
    };

    // Step 3: Confirm Payment
    const payRes = await fetch('/api/v1/Gateway/Payment', {
      method: 'POST',
      body: JSON.stringify(paymentRequest)
    });
    console.log(await payRes.json());
  }
}
```

### Dart (Flutter)

```dart
Future<void> processQrPayment(String qrString) async {
  final verifyRes = await http.post(
    Uri.parse('api/v1/Gateway/Verify'),
    body: jsonEncode({'QRCode': qrString}),
  );

  final data = jsonDecode(verifyRes.body);
  if (data['isVerified']) {
    final parsed = data['parsed'];
    final isStatic = parsed['pointOfInitializationMethod'] == "11";

    String ctgPurp = "C2CCRT";
    if (parsed['payloadFormatIndicator'] == "01") {
      ctgPurp = isStatic ? "C2BSQR" : "C2BDQR";
    }

    final payBody = {
      "agent": parsed['bankBICCode'],
      "lclInstrument": "CRTRM",
      "ctgPurp": ctgPurp,
      "amount": parsed['amount'] ?? 0.0,
      "crAccount": parsed['accountId'],
      "narration": parsed['particulars'] ?? "",
    };

    final payRes = await http.post(
      Uri.parse('api/v1/Gateway/Payment'),
      body: jsonEncode(payBody),
    );
  }
}
```

### Kotlin (Android)

```kotlin
suspend fun executeFlow(qrCode: String) {
    val verifyRes = api.verify(VerificationRequest(qrCode))
    if (verifyRes.isVerified) {
        val parsed = verifyRes.parsed
        val ctgPurp = when {
            parsed.payloadFormatIndicator == "02" -> "C2CCRT"
            parsed.pointOfInitializationMethod == "11" -> "C2BSQR"
            else -> "C2BDQR"
        }

        val request = PaymentRequest(
            agent = parsed.bankBICCode,
            lclInstrument = "CRTRM",
            ctgPurp = ctgPurp,
            amount = parsed.amount ?: 0.0,
            crAccount = parsed.accountId,
            narration = parsed.particulars ?: ""
        )

        val payRes = api.makePayment(request)
    }
}
```

### Swift (iOS)

```swift
func performQrTransaction(qr: String) async throws {
    let verifyRes = try await api.verify(qr: qr)
    guard verifyRes.isVerified else { return }
    
    let parsed = verifyRes.parsed
    var ctgPurp = "C2CCRT"
    if parsed.payloadFormatIndicator == "01" {
        ctgPurp = (parsed.pointOfInitializationMethod == "11") ? "C2BSQR" : "C2BDQR"
    }
    
    let body: [String: Any] = [
        "agent": parsed.bankBICCode,
        "lclInstrument": "CRTRM",
        "ctgPurp": ctgPurp,
        "amount": parsed.amount ?? 0.0,
        "crAccount": parsed.accountId,
        "narration": parsed.particulars ?? ""
    ]
    
    let payResult = try await api.postPayment(body: body)
}
```
