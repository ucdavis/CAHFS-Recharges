/*
  Phase 1 — Pending cash receipts view for eConnect / TraceFirst poster.

  Run on BOTH company DBs:
    - gparc  (CAHFS)  — joins local dbo.RM00101
    - gpare  (EQUINE) — joins local dbo.RM00101

  BillingId is normalized in dbo.C_LB_Build_Staging (trim spaces / pad to 8
  against RM00101). This view only includes rows whose BillingId already
  exists in RM00101.

  Failed rows are excluded until reset via 003_Reset_Failed_For_Retry.sql.
*/

SET NOCOUNT ON;
GO

CREATE OR ALTER VIEW dbo.C_LB_Pending_Cash_Receipts_V
AS
SELECT
    s.StagingId,
    s.FileName,
    RTRIM(c.CUSTNMBR) AS BillingId,
    s.CheckNumber,
    s.CheckAmount,
    s.DepositDate,
    s.LockboxNumber,
    s.BatchNumber,
    s.SeqNumber,
    s.ValidationStatus,
    s.PostStatus,
    RTRIM(c.CUSTNAME) AS CustomerName
FROM dbo.C_LB_Payment_Staging AS s
INNER JOIN dbo.RM00101 AS c
    ON RTRIM(c.CUSTNMBR) = LTRIM(RTRIM(s.BillingId))
WHERE s.ValidationStatus = 'VALID'
  AND s.IsPrimaryAddenda = 1
  AND ISNULL(s.PostStatus, '') NOT IN ('Posted', 'Failed')
  AND LEN(LTRIM(RTRIM(ISNULL(s.BillingId, '')))) > 0
  AND LEN(LTRIM(RTRIM(c.CUSTNMBR))) > 0;
GO

/*
  Optional — VALID primary rows that still do not match RM00101:

SELECT
    s.StagingId,
    s.FileName,
    s.BillingId,
    s.CheckNumber,
    s.CheckAmount,
    s.DepositDate,
    s.ValidationStatus
FROM dbo.C_LB_Payment_Staging AS s
WHERE s.ValidationStatus = 'VALID'
  AND s.IsPrimaryAddenda = 1
  AND LEN(LTRIM(RTRIM(ISNULL(s.BillingId, '')))) > 0
  AND ISNULL(s.PostStatus, '') NOT IN ('Posted', 'Failed')
  AND NOT EXISTS (
        SELECT 1
        FROM dbo.RM00101 AS c
        WHERE RTRIM(c.CUSTNMBR) = LTRIM(RTRIM(s.BillingId))
    );
*/
