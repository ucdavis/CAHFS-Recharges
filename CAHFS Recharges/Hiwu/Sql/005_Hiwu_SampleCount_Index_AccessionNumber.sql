/*
  HIWU Manifest Files: SampleCount, sample search index, AccessionNumber on cofl samples.

  Run on the CAHFS-Integrations database only.
  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.C_HIWU_Import_File', N'SampleCount') IS NULL
BEGIN
    ALTER TABLE dbo.C_HIWU_Import_File
        ADD SampleCount int NOT NULL
            CONSTRAINT DF_C_HIWU_Import_File_SampleCount DEFAULT (0);
END
GO

IF OBJECT_ID(N'dbo.C_HIWU_Import_Sample', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_HIWU_Import_Sample
    (
        FileId uniqueidentifier NOT NULL,
        SampleId nvarchar(100) NOT NULL,
        CONSTRAINT PK_C_HIWU_Import_Sample PRIMARY KEY (FileId, SampleId),
        CONSTRAINT FK_C_HIWU_Import_Sample_File
            FOREIGN KEY (FileId) REFERENCES dbo.C_HIWU_Import_File (FileId)
    );

    CREATE INDEX IX_C_HIWU_Import_Sample_SampleId
        ON dbo.C_HIWU_Import_Sample (SampleId);
END
GO

IF COL_LENGTH(N'dbo.C_HIWU_Cofl_Sample', N'AccessionNumber') IS NULL
BEGIN
    ALTER TABLE dbo.C_HIWU_Cofl_Sample
        ADD AccessionNumber nvarchar(100) NULL;
END
GO
