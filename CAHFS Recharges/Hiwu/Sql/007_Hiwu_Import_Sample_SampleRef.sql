/*
  HIWU receiving v2.0: SampleRef on C_HIWU_Import_Sample for Results round-trip.

  Run on the CAHFS-Integrations database only.
  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.C_HIWU_Import_Sample', N'SampleRef') IS NULL
BEGIN
    ALTER TABLE dbo.C_HIWU_Import_Sample
        ADD SampleRef nvarchar(100) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_C_HIWU_Import_Sample_SampleRef'
      AND object_id = OBJECT_ID(N'dbo.C_HIWU_Import_Sample')
)
BEGIN
    CREATE INDEX IX_C_HIWU_Import_Sample_SampleRef
        ON dbo.C_HIWU_Import_Sample (SampleRef)
        WHERE SampleRef IS NOT NULL;
END
GO
