/*
  HIWU TraceFirst order history.

  Run on the CAHFS-Integrations database only.

  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.C_HIWU_Cofl_Sample', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_HIWU_Cofl_Sample
    (
        FileId uniqueidentifier NOT NULL,
        SampleKey nvarchar(100) NOT NULL,
        AccessionId bigint NULL,
        AnimalId bigint NULL,
        SpecimenId bigint NULL,
        Status nvarchar(20) NOT NULL,
        ErrorMessage nvarchar(500) NULL,
        UpdatedUtc datetime2(3) NOT NULL,
        CONSTRAINT PK_C_HIWU_Cofl_Sample PRIMARY KEY (FileId, SampleKey),
        CONSTRAINT FK_C_HIWU_Cofl_Sample_File
            FOREIGN KEY (FileId) REFERENCES dbo.C_HIWU_Import_File (FileId)
    );
END
GO
