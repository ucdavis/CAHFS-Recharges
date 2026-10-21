/*
  HIWU amendment sample review history.
  One row is the first time an operator marked that sample reviewed.

  Run on the CAHFS-Integrations database only.

  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.C_HIWU_Sample_Review', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_HIWU_Sample_Review
    (
        FileId uniqueidentifier NOT NULL,
        SampleKey nvarchar(100) NOT NULL,
        ReviewedBy nvarchar(128) NOT NULL,
        ReviewedUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_C_HIWU_Sample_Review PRIMARY KEY (FileId, SampleKey),
        CONSTRAINT FK_C_HIWU_Sample_Review_File
            FOREIGN KEY (FileId) REFERENCES dbo.C_HIWU_Import_File (FileId)
    );
END
GO
