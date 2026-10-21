/*
  HIWU results report history. SFTP upload is not written yet.
  The report XML is stored here until it can be sent.

  Run on the CAHFS-Integrations database only.

  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.C_HIWU_Report', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_HIWU_Report
    (
        FileId uniqueidentifier NOT NULL
            CONSTRAINT PK_C_HIWU_Report PRIMARY KEY,
        AccessionId bigint NOT NULL,
        Status nvarchar(20) NOT NULL,
        RawJson nvarchar(max) NOT NULL,
        ReportXml nvarchar(max) NULL,
        ErrorMessage nvarchar(500) NULL,
        UpdatedUtc datetime2(3) NOT NULL,
        CONSTRAINT FK_C_HIWU_Report_File
            FOREIGN KEY (FileId) REFERENCES dbo.C_HIWU_Import_File (FileId)
    );
END
GO
