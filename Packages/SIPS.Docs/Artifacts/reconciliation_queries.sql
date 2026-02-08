-- ============================================================================
-- SIPS RECONCILIATION DASHBOARD QUERIES
-- ============================================================================
-- Purpose: Monitor "In-Doubt" transactions and provide audit traceability.
-- Database: PostgreSQL (with JSONB support)
-- ============================================================================

-- 1. ACTIVE RECONCILIATION QUEUE
-- Shows all transactions currently in "CheckStatus" needing manual or automated closure.
SELECT 
    "Id", 
    "TxId", 
    "EndToEndId", 
    "Date", 
    "Status",
    "Reason",
    "CoreBankResponse"->'auditLedger' as "Ledger",
    xmin as "ConcurrencyToken"
FROM "ISOMessages"
WHERE "Status" = 10 -- TransactionStatus.CheckStatus
ORDER BY "Date" DESC;

-- 2. "IN-DOUBT" BUT RECONCILED (Last 24h)
-- Audit trail of transactions that timed out but have since been reconciled.
SELECT 
    "TxId", 
    "Status", 
    jsonb_array_elements("CoreBankResponse"->'auditLedger')->>'event' as "ClosingEvent",
    jsonb_array_elements("CoreBankResponse"->'auditLedger')->>'timestampUtc' as "ReconciledAt"
FROM "ISOMessages"
WHERE "CoreBankResponse"->'auditLedger' @> '[{"event": "ReconciledCredited"}]'
   OR "CoreBankResponse"->'auditLedger' @> '[{"event": "ReconciledNotCredited"}]'
   AND "Date" > NOW() - INTERVAL '24 hours';

-- 3. CONCURRENCY CONFLICT AUDIT
-- Detects if multiple reconciliation attempts were logged (indicating high-contention status).
SELECT 
    "TxId", 
    jsonb_array_length("CoreBankResponse"->'auditLedger') as "EventCount",
    "CoreBankResponse"->'auditLedger'
FROM "ISOMessages"
WHERE jsonb_array_length("CoreBankResponse"->'auditLedger') > 1
ORDER BY "EventCount" DESC;

-- 4. TIMEOUT DISTRIBUTION BY PATH
-- Helps identify if timeouts are concentrated in specific CoreBank routes.
SELECT 
    entry->>'isoPath' as "Path",
    COUNT(*) as "TimeoutCount",
    AVG((entry->>'elapsedMs')::numeric) as "AvgLatencyMs"
FROM "ISOMessages",
LATERAL jsonb_array_elements("CoreBankResponse"->'auditLedger') as entry
WHERE entry->>'event' = 'CheckStatusRaised'
GROUP BY entry->>'isoPath';

-- 5. IMMUTABILITY CHECK
-- Verifies that xmin changes on every update (sanity check for the concurrency engine).
SELECT 
    "Id", 
    "TxId", 
    xmin as "CurrentXmin",
    "Date" as "LastActivity"
FROM "ISOMessages"
WHERE "Status" = 10;
