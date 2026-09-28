/*
  HIWU manifest history.

  Run on the CAHFS-Integrations database only.

  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.C_HIWU_Ingest_Run', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_HIWU_Ingest_Run
    (
        IngestRunId uniqueidentifier NOT NULL
            CONSTRAINT PK_C_HIWU_Ingest_Run PRIMARY KEY,
        StartedUtc datetime2(3) NOT NULL,
        CompletedUtc datetime2(3) NULL,
        Status nvarchar(20) NOT NULL,
        FilesStored int NOT NULL
            CONSTRAINT DF_C_HIWU_Ingest_Run_FilesStored DEFAULT (0),
        FilesDuplicate int NOT NULL
            CONSTRAINT DF_C_HIWU_Ingest_Run_FilesDuplicate DEFAULT (0),
        FilesFailed int NOT NULL
            CONSTRAINT DF_C_HIWU_Ingest_Run_FilesFailed DEFAULT (0),
        ErrorSummary nvarchar(500) NULL
    );
END
GO

IF OBJECT_ID(N'dbo.C_HIWU_Import_File', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_HIWU_Import_File
    (
        FileId uniqueidentifier NOT NULL
            CONSTRAINT PK_C_HIWU_Import_File PRIMARY KEY,
        RemoteFileName nvarchar(500) NOT NULL,
        ReceivedUtc datetime2(3) NOT NULL,
        RawXml nvarchar(max) NOT NULL,
        SizeBytes bigint NOT NULL,
        ContentHash nchar(64) NOT NULL,
        ParseStatus nvarchar(20) NOT NULL,
        ParseError nvarchar(500) NULL,
        IsAmendment bit NOT NULL
            CONSTRAINT DF_C_HIWU_Import_File_IsAmendment DEFAULT (0),
        OriginalFileId uniqueidentifier NULL,
        CONSTRAINT UX_C_HIWU_Import_File_ContentHash UNIQUE (ContentHash),
        CONSTRAINT FK_C_HIWU_Import_File_Original
            FOREIGN KEY (OriginalFileId) REFERENCES dbo.C_HIWU_Import_File (FileId)
    );
END
GO
