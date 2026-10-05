/*
  Stores the exact journal JSON posted to Aggie Enterprise.

  Run on both financial databases (CAHFS and EQUINE).
  Safe to re-run.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.C_AE_Journal_Payload', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.C_AE_Journal_Payload
    (
        PayloadID uniqueidentifier NOT NULL
            CONSTRAINT PK_C_AE_Journal_Payload PRIMARY KEY,
        batchID uniqueidentifier NOT NULL,
        SentUtc datetime2(3) NOT NULL,
        PayloadJson nvarchar(max) NOT NULL
    );

    CREATE INDEX IX_C_AE_Journal_Payload_batchID
        ON dbo.C_AE_Journal_Payload (batchID, SentUtc DESC);
END
GO
