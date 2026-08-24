/*
  Optional helper — clear Failed so rows re-enter C_LB_Pending_Cash_Receipts_V.

  Failed rows are excluded from the pending view. After you fix the root cause
  (customer created, checkbook fixed, etc.), run this reset so they post again.

  Run on the target company DB (gparc or gpare).
*/

-- Preview
SELECT StagingId, BillingId, CheckNumber, CheckAmount, PostStatus, PostError, PostedAt
FROM dbo.C_LB_Payment_Staging
WHERE PostStatus = 'Failed'
ORDER BY PostedAt DESC;

-- Reset (uncomment to execute)
/*
UPDATE dbo.C_LB_Payment_Staging
SET PostStatus = NULL,
    PostedAt = NULL,
    PostError = NULL,
    ExternalDocNumber = NULL
WHERE PostStatus = 'Failed';
*/
