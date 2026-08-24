# Lockbox Cash Receipt — Phase 1 SQL

Run these scripts on **both** GP company databases (same targets CAEI uses):

| App connection | Company | Database |
|---|---|---|
| `FinancialDb` | CAHFS | `gparc` |
| `EquineFinancialDb` | EQUINE | `gpare` |

## Order

1. [`001_C_LB_Payment_Staging_PostStatus.sql`](001_C_LB_Payment_Staging_PostStatus.sql) — post columns
2. [`005_C_LB_Build_Staging_Normalize_BillingId.sql`](005_C_LB_Build_Staging_Normalize_BillingId.sql) — build staging + BillingId normalize + `INVALID_CUSTOMER`
3. [`002_C_LB_Pending_Cash_Receipts_V.sql`](002_C_LB_Pending_Cash_Receipts_V.sql) — pending view (`VALID` + RM00101 only)
4. [`006_Mark_Invalid_Customer_Status.sql`](006_Mark_Invalid_Customer_Status.sql) — optional backfill for existing rows
5. [`003_Reset_Failed_For_Retry.sql`](003_Reset_Failed_For_Retry.sql) — optional Failed → retry

## ValidationStatus values

| Status | Meaning |
|---|---|
| `VALID` | Trailer OK and BillingId matches `RM00101` |
| `WARN` | No addenda (manual billing match) |
| `INVALID_CUSTOMER` | BillingId blank or not in `RM00101` — held from GP post; fix on **Check Validations** |
| `ERROR` | Trailer amount/count mismatch |
| `PENDING` | Intermediate during build |

## Pending poster rules

Only `ValidationStatus = 'VALID'` (+ primary addenda, not Posted/Failed, RM00101 match) enters `C_LB_Pending_Cash_Receipts_V`.

## Verify

```sql
SELECT ValidationStatus, COUNT(*)
FROM dbo.C_LB_Payment_Staging
GROUP BY ValidationStatus;

SELECT TOP 20 *
FROM dbo.C_LB_Pending_Cash_Receipts_V
ORDER BY DepositDate DESC, StagingId;
```
