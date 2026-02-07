# SmartVista Protocol Mapping Matrix (AUTHORITATIVE)

**Date**: February 7, 2026  
**Status**: ✅ Approved for Implementation  
**Source**: SmartVista IPS "How-to / API Standard" + BPC Somalia Payment System spec

---

## Core Principle

- **Technical/Protocol Failure** → **admi.002**
- **Business Processing Outcome** → **pacs.002**
- **Replay** → Replay exactly what was previously returned

---

## IncomingTransactionHandler — Protocol-Correct Mapping

| Path | ISO Response | Reason/Status | SmartVista Semantics |
|------|-------------|---------------|---------------------|
| **ParseFail** | admi.002 | `InvalidXML` OR `SignatureInvalid` | Invalid XML/signature = technical reject |
| **MissingTxId** | admi.002 | `MandatoryElementMissing` | Mandatory element missing → protocol invalid |
| **DbInsertFailed** | admi.002 | `TechnicalError` | Infrastructure/persistence failure |
| **ReplayStored** | Replay stored | Preserve original | Deterministic replay required |
| **ReplayAfterWait** | Replay stored | Preserve original | Owner completed during wait |
| **DuplicatePending** | admi.002 | `DuplicateMessageInProcess` | Duplicate message in-process (not business decision) |
| **Normal (Owner)** | pacs.002 | `ACSC` / `RJCT` | Business outcome of payment |
| **CoreBankTimeout** | pacs.002 | `RJCT` + `SystemUnavailable`, Status=`CheckStatus` | Business system unavailable, payment indeterminate |
| **CoreBankError** | pacs.002 | `RJCT` + `SystemUnavailable` | Business system unavailable |
| **EmergencyCatch** | admi.002 | `TechnicalError` | Catastrophic technical failure |

---

## Index Strategy (Production-Grade)

```sql
-- Transaction-level idempotency
CREATE UNIQUE INDEX ux_iso_msg_type_txid
ON "ISOMessages" ("MessageType", "TxId")
WHERE "TxId" IS NOT NULL AND "TxId" <> '';

-- Message-level idempotency
CREATE UNIQUE INDEX ux_iso_msg_type_msgid
ON "ISOMessages" ("MessageType", "MsgId")
WHERE "MsgId" IS NOT NULL AND "MsgId" <> '';

-- Audit/correlation
CREATE INDEX ix_iso_msg_uetr
ON "ISOMessages" ("UETR");
```

---

## MsgId Conflict Handling

**Rule**: 
- **MsgId duplicate AND Response exists** → Replay stored response
- **MsgId duplicate AND no Response yet** → admi.002 (DuplicateMessageID)

This keeps MsgId behavior symmetrical with TxId and preserves deterministic replay.

---

## Audit Enhancement

Log which uniqueness constraint fired:
```json
{
  "event": "DuplicateDetected",
  "duplicateBy": "TxId" | "MsgId",
  "correlation": { ... }
}
```

This saves hours during regulator/participant disputes.
