/*
  One-time / ops: mark existing staging rows with blank or unmatched BillingId
  as INVALID_CUSTOMER (or WARN when no addenda).

  Run on gparc and gpare.
  Preview first, then uncomment the UPDATEs.
*/

SET NOCOUNT ON;

-- Preview: blank BillingId still VALID (bug before this fix)
SELECT StagingId, FileName, BillingId, HasAddenda, ValidationStatus, CheckNumber
FROM dbo.C_LB_Payment_Staging
WHERE ISNULL(PostStatus, '') <> 'Posted'
  AND ValidationStatus = 'VALID'
  AND LTRIM(RTRIM(ISNULL(BillingId, ''))) = ''
ORDER BY DepositDate DESC, StagingId;

-- 1) No addenda + blank/any → WARN (manual match)
/*
UPDATE dbo.C_LB_Payment_Staging
SET
    ValidationStatus = 'WARN',
    ValidationNotes  = 'No addenda record — manual billing reference matching required'
WHERE HasAddenda = 0
  AND ISNULL(PostStatus, '') <> 'Posted'
  AND ValidationStatus IN ('VALID', 'PENDING', 'WARN');
*/

-- 2) Has addenda + blank BillingId → INVALID_CUSTOMER
/*
UPDATE dbo.C_LB_Payment_Staging
SET
    ValidationStatus = 'INVALID_CUSTOMER',
    ValidationNotes  = 'Billing ID is blank — enter a valid GP customer (CUSTNMBR) before posting.'
WHERE HasAddenda = 1
  AND ISNULL(PostStatus, '') <> 'Posted'
  AND ValidationStatus IN ('VALID', 'PENDING', 'INVALID_CUSTOMER')
  AND LTRIM(RTRIM(ISNULL(BillingId, ''))) = '';
*/

-- 3) Has addenda + non-blank BillingId not in RM00101 → INVALID_CUSTOMER
/*
UPDATE s
SET
    s.ValidationStatus = 'INVALID_CUSTOMER',
    s.ValidationNotes = LEFT(
        'Customer Number (CUSTNMBR) "'
        + LTRIM(RTRIM(s.BillingId))
        + '" was not found in RM00101. Correct Billing ID before GP posting.',
        500)
FROM dbo.C_LB_Payment_Staging s
WHERE s.HasAddenda = 1
  AND ISNULL(s.PostStatus, '') <> 'Posted'
  AND s.ValidationStatus IN ('VALID', 'PENDING', 'INVALID_CUSTOMER')
  AND LTRIM(RTRIM(ISNULL(s.BillingId, ''))) <> ''
  AND NOT EXISTS (
        SELECT 1
        FROM dbo.RM00101 rm
        WHERE LEN(LTRIM(RTRIM(rm.CUSTNMBR))) > 0
          AND RTRIM(rm.CUSTNMBR) = LTRIM(RTRIM(s.BillingId))
      );
*/
