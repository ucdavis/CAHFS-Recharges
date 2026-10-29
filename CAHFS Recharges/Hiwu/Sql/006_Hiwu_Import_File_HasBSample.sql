/*
  HIWU Manifest Files: HasBSample flag on C_HIWU_Import_File.

  Run on the CAHFS-Integrations database only.
  Safe to re-run.

  Note: B-Sample is a manifest field, not stored on C_HIWU_Cofl_Sample.
  New/updated ingest sets HasBSample from parsed XML. Existing rows stay NULL
  until the app backfill (BackfillSampleIndexAsync) evaluates them once.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.C_HIWU_Import_File', N'HasBSample') IS NULL
BEGIN
    ALTER TABLE dbo.C_HIWU_Import_File
        ADD HasBSample bit NULL;
END
GO
