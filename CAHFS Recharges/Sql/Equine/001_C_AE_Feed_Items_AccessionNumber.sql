-- Adds AccessionNumber for Glide UdfString3 (from SOP10106.USERDEF1).
-- Required on gpare before the updated Equine feed proc.
-- Also run on gparc (CAHFS) before deploying the CAEI app, so EF can read both DBs.

USE [gpare];
GO

IF COL_LENGTH('dbo.C_AE_Feed_Items', 'AccessionNumber') IS NULL
BEGIN
    ALTER TABLE dbo.C_AE_Feed_Items
        ADD AccessionNumber nvarchar(50) NULL;
END
GO

-- For CAHFS use Sql/Cahfs/001_C_AE_Feed_Items_AccessionNumber.sql on gparc.

