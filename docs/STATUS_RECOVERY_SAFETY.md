# Domestic status and recovery safety

## Test-first evidence

Added `StatusRecoverySafetyTests` before changing the status handler. All 17 initial behavioral cases failed against the existing code. Five additional IPS status-mapping cases were then added. The final focused suite passes all 22 cases.

| Review finding | Regression coverage | Corrected behavior |
|---|---|---|
| Recovery exhaustion returned cached rejection | Real `FinalizeAfterMaxRetriesAsync`, followed by status enquiry | `PDNG` with reconciliation notice unless stored explicit rejection matches the original references |
| Non-final/unknown network reports became failure | Two consecutive enquiries for PDNG, ACSP, ACCP, unknown and missing status; mapper tests | No completion callback; remain eligible for reconciliation; no cached rejection |
| Invalid-signature XML continued into final processing | Signature failure with final-looking ACSC and RJCT bodies | Stop before business interpretation; no final persistence or notification |
| Report references were not correlated | Signed final reports with wrong TxId or EndToEndId | Original stays unresolved; no completion callback |
| Positive final outcomes must still work | Verified, correlated ACSC and RJCT, followed by a cached enquiry | Evidence persisted; one bank notification; cached original outcome |
| Transport errors must not establish finality | Timeout, gateway error and server error with final-looking body | `PDNG`; no final callback |

The shared `DomesticStatusEvidence` reader requires one ISO transaction-status entry, explicit TxSts and matching original transaction/end-to-end references. The general parser's missing-status RJCT default is not accepted as evidence. Live callers verify the network signature before using the reader; it is not itself a signature verifier.

## Validation

```bash
dotnet test Packages/SIPS.Core.Tests/SIPS.Core.Tests.csproj -m:1 \
  --filter FullyQualifiedName~StatusRecoverySafetyTests
```

Full core suite: 237 passed, 7 existing skips, 0 failed. API suite: 250 passed, 1 known pre-existing captured-recall certificate failure (previously reproduced on unchanged main). The dedicated domestic payment PostgreSQL suite also passed all 8 cases, covering atomic retries and historical migration compatibility.

The status reviewer rechecked the implementation and confirmed all four original P1 findings are resolved, with no new publication blocker identified in these fixes.

## Remaining boundary

An existing liveness issue remains when verified network success is followed by bank notification failure, recovery exhaustion, and internal Failed state. The status endpoint conservatively returns pending/manual reconciliation rather than falsely rejecting it. Separating the confirmed network outcome from notification-delivery state and proving notification recovery is a follow-up enhancement. This fix does not claim that bank notification is guaranteed to complete automatically after its retry budget is exhausted.

No additional schema migration is introduced by these status fixes. They are local source changes and are not included in the existing numbered image release until a new release is explicitly published.
