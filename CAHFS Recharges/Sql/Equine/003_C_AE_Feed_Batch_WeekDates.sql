-- Equine (gpare): week-slice dates for daily find-or-create batch load.
USE [gpare];
GO

IF COL_LENGTH('dbo.C_AE_Feed_Batch', 'WeekStartDate') IS NULL
BEGIN
    ALTER TABLE dbo.C_AE_Feed_Batch ADD WeekStartDate date NULL;
END
GO

IF COL_LENGTH('dbo.C_AE_Feed_Batch', 'WeekEndDate') IS NULL
BEGIN
    ALTER TABLE dbo.C_AE_Feed_Batch ADD WeekEndDate date NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_C_AE_Feed_Batch_WeekDates'
      AND object_id = OBJECT_ID('dbo.C_AE_Feed_Batch')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_C_AE_Feed_Batch_WeekDates
        ON dbo.C_AE_Feed_Batch (WeekStartDate, WeekEndDate);
END
GO
