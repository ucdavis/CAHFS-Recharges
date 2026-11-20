-- CAHFS (gparc) staging schema
-- Adds AccessionNumber for Glide UdfString3 (from SOP10106.USERDEF1).
-- Run on gparc before deploying the updated C_Load_AE_Feed_Tables_Weekly_V2.

USE [gparc];
GO

IF COL_LENGTH('dbo.C_AE_Feed_Items', 'AccessionNumber') IS NULL
BEGIN
    ALTER TABLE dbo.C_AE_Feed_Items
        ADD AccessionNumber nvarchar(50) NULL;
END
GO
