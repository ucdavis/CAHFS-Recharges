/*
  Phase 1 — Add cash-receipt post status columns to C_LB_Payment_Staging.

  Run on BOTH company DBs used by CAEI:
    - gparc  (CAHFS  / FinancialDb)
    - gpare  (EQUINE / EquineFinancialDb)

  Safe to re-run (adds only missing columns).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.C_LB_Payment_Staging', 'PostStatus') IS NULL
BEGIN
    ALTER TABLE dbo.C_LB_Payment_Staging
        ADD PostStatus varchar(20) NULL;
END
GO

IF COL_LENGTH('dbo.C_LB_Payment_Staging', 'PostedAt') IS NULL
BEGIN
    ALTER TABLE dbo.C_LB_Payment_Staging
        ADD PostedAt datetime2(3) NULL;
END
GO

IF COL_LENGTH('dbo.C_LB_Payment_Staging', 'PostError') IS NULL
BEGIN
    ALTER TABLE dbo.C_LB_Payment_Staging
        ADD PostError varchar(1000) NULL;
END
GO

IF COL_LENGTH('dbo.C_LB_Payment_Staging', 'ExternalDocNumber') IS NULL
BEGIN
    ALTER TABLE dbo.C_LB_Payment_Staging
        ADD ExternalDocNumber varchar(50) NULL;
END
GO

/*
  PostStatus values (application convention):
    NULL / '' / 'Pending'  = not posted yet (eligible if other filters pass)
    'Posted'               = successfully posted to GP (or later TraceFirst)
    'Failed'               = last post attempt failed (may be retried after reset)
*/
