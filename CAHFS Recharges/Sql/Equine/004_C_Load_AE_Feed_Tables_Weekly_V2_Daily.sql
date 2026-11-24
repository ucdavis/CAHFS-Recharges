/*
  Equine (gpare) — daily month-slice batch load
  Replaces the WHILE-loop weekly builder in C_Load_AE_Feed_Tables_Weekly_V2.

  Behavior:
  - Runs for "today" (or optional @AsOfDate)
  - Month slices: 1-7, 8-14, 15-21, 22-28, 29-EOM
  - Find-or-create one batch per slice; later days reload into same batch
  - Filter / transactionDate = POSTEDDT (Posted date)
  - HISTORY + OPEN (same SELECT pattern as validated CAHFS; VHFS501)
  - Returns: SOPTYPE=4 reverse COA + signed amount (-1 * OXTNDPRC)
  - AccessionNumber = SOP10106.USERDEF1

  Deploy order on gpare:
  1) 001_C_AE_Feed_Items_AccessionNumber.sql
  2) 003_C_AE_Feed_Batch_WeekDates.sql
  3) This script (CREATE OR ALTER PROCEDURE)
*/

USE [gpare];
GO

CREATE OR ALTER PROCEDURE [dbo].[C_Load_AE_Feed_Tables_Weekly_V2]
    @AsOfDate date = NULL  -- optional backfill; default = today
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE
          @currentStartDate   date
        , @currentEndDate     date
        , @today              date
        , @monthStart         date
        , @monthEnd           date
        , @dom                int
        , @AEJournalReference nvarchar(25)
        , @AEJournalName      nvarchar(100)
        , @batchID            uniqueidentifier
        , @alreadySent        bit = 0;

    SET @today = ISNULL(@AsOfDate, CAST(GETDATE() AS date));
    SET @monthStart = DATEFROMPARTS(YEAR(@today), MONTH(@today), 1);
    SET @monthEnd   = EOMONTH(@today);
    SET @dom        = DAY(@today);

    /* Month-bounded week slices */
    IF @dom BETWEEN 1 AND 7
    BEGIN
        SET @currentStartDate = @monthStart;
        SET @currentEndDate   = DATEFROMPARTS(YEAR(@today), MONTH(@today), 7);
    END
    ELSE IF @dom BETWEEN 8 AND 14
    BEGIN
        SET @currentStartDate = DATEFROMPARTS(YEAR(@today), MONTH(@today), 8);
        SET @currentEndDate   = DATEFROMPARTS(YEAR(@today), MONTH(@today), 14);
    END
    ELSE IF @dom BETWEEN 15 AND 21
    BEGIN
        SET @currentStartDate = DATEFROMPARTS(YEAR(@today), MONTH(@today), 15);
        SET @currentEndDate   = DATEFROMPARTS(YEAR(@today), MONTH(@today), 21);
    END
    ELSE IF @dom BETWEEN 22 AND 28
    BEGIN
        SET @currentStartDate = DATEFROMPARTS(YEAR(@today), MONTH(@today), 22);
        SET @currentEndDate   = DATEFROMPARTS(YEAR(@today), MONTH(@today), 28);
    END
    ELSE
    BEGIN
        /* 29–EOM (short final slice) */
        SET @currentStartDate = DATEFROMPARTS(YEAR(@today), MONTH(@today), 29);
        SET @currentEndDate   = @monthEnd;
    END;

    DECLARE @dt varchar(23) =
        CONVERT(varchar(23), DATEADD(MONTH, -24, GETDATE()), 121);

    /* STEP 1 — Pull STARLIMS CHARGENO Values */
    IF OBJECT_ID('tempdb..#Starlims') IS NOT NULL DROP TABLE #Starlims;

    CREATE TABLE #Starlims
    (
        DOCNUMBER   varchar(100) NULL,
        RawChargeNo varchar(200) NULL,
        FIRSTNAME   varchar(100) NULL,
        LASTNAME    varchar(100) NULL,
        ORGANIZNAME varchar(200) NULL
    );

    DECLARE @sql nvarchar(max);
    SET @sql = N'
INSERT INTO #Starlims (DOCNUMBER, RawChargeNo, FIRSTNAME, LASTNAME, ORGANIZNAME)
SELECT
    CAST(OQ.FOLDERNO AS varchar(50)) + CAST(OQ.INVOICEID AS varchar(50)) AS DOCNUMBER,
    LTRIM(RTRIM(OQ.CHARGENO)) AS RawChargeNo,
    OQ.FIRSTNAME,
    OQ.LASTNAME,
    COALESCE(OQ.ORGANIZNAME, OQ.COMPANYNAME) AS ORGANIZNAME
FROM OPENQUERY([starlims-data], ''SELECT
    A.FOLDERNO,
    A.INVOICEID,
    A.CHARGENO,
    B.FIRSTNAME,
    B.LASTNAME,
    B.ORGANIZNAME,
    B.COMPANYNAME
FROM Prod_data.dbo.C_GP_Invoices A
LEFT JOIN Prod_data.dbo.C_RASCLIENTS_DETAILS_CUR_V B
    ON A.BILLEDTO = B.RASCLIENTID
WHERE A.InvoiceDate >= ''''' + @dt + ''''' '') AS OQ;';

    EXEC sys.sp_executesql @sql;

    /* STEP 2 — Normalize, Clean, Get Lengths */
    IF OBJECT_ID('tempdb..#Clean') IS NOT NULL DROP TABLE #Clean;

    SELECT
        DOCNUMBER,
        LTRIM(RTRIM(RawChargeNo)) AS RawChargeNo,
        FIRSTNAME,
        LASTNAME,
        ORGANIZNAME,
        RawLength   = LEN(RawChargeNo),
        HasDashes   = CASE WHEN RawChargeNo LIKE '%-%' THEN 1 ELSE 0 END,
        CleanChargeNo =
            UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                RawChargeNo,' ',''),'-',''),'/',''),CHAR(9),''),CHAR(13),'')
            ),
        CleanLength =
            LEN(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                RawChargeNo,' ',''),'-',''),'/',''),CHAR(9),''),CHAR(13),''))
            )
    INTO #Clean
    FROM #Starlims;

    /* STEP 3 — COA Format Detection + Conversion */
    IF OBJECT_ID('tempdb..#COA') IS NOT NULL DROP TABLE #COA;

    CREATE TABLE #COA
    (
        DOCNUMBER      varchar(50),
        RawChargeNo    varchar(200),
        RawLength      int,
        CleanChargeNo  varchar(200),
        CleanLength    int,
        HasDashes      bit,
        SegmentCount   int,
        SegmentType    varchar(20),
        Converted      varchar(300),
        NaturalAccount varchar(10),
        FIRSTNAME      varchar(100) NULL,
        LASTNAME       varchar(100) NULL,
        ORGANIZNAME    varchar(200) NULL
    );

    INSERT INTO #COA
    SELECT
        DOCNUMBER,
        RawChargeNo,
        RawLength,
        CleanChargeNo,
        CleanLength,
        HasDashes,
        SegmentCount =
            CASE WHEN HasDashes = 1
                THEN LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1
                ELSE NULL
            END,
        SegmentType =
            CASE
                WHEN (
                        UPPER(LEFT(RawChargeNo, 3)) = 'GL-'
                        OR (UPPER(LEFT(RawChargeNo, 3)) <> 'GL-'
                            AND UPPER(LEFT(RawChargeNo, 4)) <> 'PPM-'
                            AND LEFT(CleanChargeNo, 3) NOT LIKE '%[^0-9]%')
                     )
                THEN
                    CASE
                        WHEN RawChargeNo IS NULL THEN NULL
                        WHEN HasDashes = 0 THEN 'GL_REVIEW'
                        WHEN HasDashes = 1 AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 8  THEN 'GL8'
                        WHEN HasDashes = 1 AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 11 THEN 'GL11'
                        WHEN HasDashes = 1 AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 9  THEN 'GLPPM9'
                        ELSE 'INVALID'
                    END
                WHEN (
                        UPPER(LEFT(RawChargeNo, 4)) = 'PPM-'
                        OR (UPPER(LEFT(RawChargeNo, 3)) <> 'GL-'
                            AND UPPER(LEFT(RawChargeNo, 4)) <> 'PPM-'
                            AND LEFT(CleanChargeNo, 3) LIKE '%[^0-9]%')
                     )
                THEN
                    CASE
                        WHEN RawChargeNo IS NULL THEN NULL
                        WHEN HasDashes = 0 THEN 'PPM_REVIEW'
                        WHEN HasDashes = 1 AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 4 THEN 'PPM4'
                        WHEN HasDashes = 1 AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 6 THEN 'PPM6'
                        ELSE 'INVALID'
                    END
                ELSE 'INVALID'
            END,
        Converted =
            CASE
                WHEN (
                        UPPER(LEFT(RawChargeNo, 3)) = 'GL-'
                        OR (UPPER(LEFT(RawChargeNo, 3)) <> 'GL-'
                            AND UPPER(LEFT(RawChargeNo, 4)) <> 'PPM-'
                            AND LEFT(CleanChargeNo, 3) NOT LIKE '%[^0-9]%')
                     )
                THEN CASE
                        WHEN UPPER(LEFT(RawChargeNo, 3)) = 'GL-'
                            THEN SUBSTRING(RawChargeNo, 4, LEN(RawChargeNo) - 3)
                        ELSE RawChargeNo
                     END
                WHEN (
                        UPPER(LEFT(RawChargeNo, 4)) = 'PPM-'
                        OR (UPPER(LEFT(RawChargeNo, 3)) <> 'GL-'
                            AND UPPER(LEFT(RawChargeNo, 4)) <> 'PPM-'
                            AND LEFT(CleanChargeNo, 3) LIKE '%[^0-9]%')
                     )
                     AND HasDashes = 0
                THEN RawChargeNo
                WHEN (
                        UPPER(LEFT(RawChargeNo, 4)) = 'PPM-'
                        OR (UPPER(LEFT(RawChargeNo, 3)) <> 'GL-'
                            AND UPPER(LEFT(RawChargeNo, 4)) <> 'PPM-'
                            AND LEFT(CleanChargeNo, 3) LIKE '%[^0-9]%')
                     )
                     AND HasDashes = 1
                     AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 4
                THEN (
                    SELECT
                        CASE
                            WHEN LEN(S1) = 10 AND LEN(S2) = 7 AND LEN(S3) = 6 AND LEN(S4) = 6
                                THEN S1 + '-' + S4 + '-' + S2 + '-' + S3
                            WHEN LEN(S1) = 10 AND LEN(S2) = 6 AND LEN(S3) = 7 AND LEN(S4) = 6
                                THEN BaseRaw
                            ELSE BaseRaw
                        END
                    FROM (
                        SELECT BaseRaw = CASE
                                            WHEN UPPER(LEFT(RawChargeNo, 4)) = 'PPM-'
                                                THEN SUBSTRING(RawChargeNo, 5, LEN(RawChargeNo) - 4)
                                            ELSE RawChargeNo
                                         END
                    ) b
                    CROSS APPLY (
                        SELECT
                            D1 = CHARINDEX('-', BaseRaw, 1),
                            D2 = CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, 1) + 1),
                            D3 = CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, 1) + 1) + 1)
                    ) d
                    CROSS APPLY (
                        SELECT
                            S1 = SUBSTRING(BaseRaw, 1,    D1 - 1),
                            S2 = SUBSTRING(BaseRaw, D1+1, D2 - D1 - 1),
                            S3 = SUBSTRING(BaseRaw, D2+1, D3 - D2 - 1),
                            S4 = SUBSTRING(BaseRaw, D3+1, LEN(BaseRaw) - D3)
                    ) s
                )
                WHEN (
                        UPPER(LEFT(RawChargeNo, 4)) = 'PPM-'
                        OR (UPPER(LEFT(RawChargeNo, 3)) <> 'GL-'
                            AND UPPER(LEFT(RawChargeNo, 4)) <> 'PPM-'
                            AND LEFT(CleanChargeNo, 3) LIKE '%[^0-9]%')
                     )
                     AND HasDashes = 1
                     AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 = 6
                THEN (
                    SELECT
                        CASE
                            WHEN LEN(S1) = 10 AND LEN(S2) = 7 AND LEN(S3) = 6 AND LEN(S4) = 6
                             AND LEN(S5) = 7 AND LEN(S6) BETWEEN 5 AND 10
                                THEN S1 + '-' + S4 + '-' + S2 + '-' + S3 + '-' + S5 + '-' + S6
                            WHEN LEN(S1) = 10 AND LEN(S2) = 6 AND LEN(S3) = 7 AND LEN(S4) = 6
                             AND LEN(S5) = 7 AND LEN(S6) BETWEEN 5 AND 10
                                THEN BaseRaw
                            ELSE BaseRaw
                        END
                    FROM (
                        SELECT BaseRaw = CASE
                                            WHEN UPPER(LEFT(RawChargeNo, 4)) = 'PPM-'
                                                THEN SUBSTRING(RawChargeNo, 5, LEN(RawChargeNo) - 4)
                                            ELSE RawChargeNo
                                         END
                    ) b
                    CROSS APPLY (
                        SELECT
                            D1 = CHARINDEX('-', BaseRaw, 1),
                            D2 = CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, 1) + 1),
                            D3 = CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, 1) + 1) + 1),
                            D4 = CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, 1) + 1) + 1) + 1),
                            D5 = CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, CHARINDEX('-', BaseRaw, 1) + 1) + 1) + 1) + 1)
                    ) d
                    CROSS APPLY (
                        SELECT
                            S1 = SUBSTRING(BaseRaw, 1,    D1 - 1),
                            S2 = SUBSTRING(BaseRaw, D1+1, D2 - D1 - 1),
                            S3 = SUBSTRING(BaseRaw, D2+1, D3 - D2 - 1),
                            S4 = SUBSTRING(BaseRaw, D3+1, D4 - D3 - 1),
                            S5 = SUBSTRING(BaseRaw, D4+1, D5 - D4 - 1),
                            S6 = SUBSTRING(BaseRaw, D5+1, LEN(BaseRaw) - D5)
                    ) s
                )
                ELSE RawChargeNo
            END,
        NaturalAccount =
            CASE
                WHEN HasDashes = 0 THEN NULL
                WHEN LEFT(CleanChargeNo,3) NOT LIKE '%[^0-9]%'
                     AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 NOT IN (8,9,11)
                    THEN NULL
                WHEN LEFT(CleanChargeNo,3) LIKE '%[^0-9]%'
                     AND LEN(RawChargeNo) - LEN(REPLACE(RawChargeNo,'-','')) + 1 NOT IN (4,6)
                    THEN NULL
                ELSE (
                    SELECT SUBSTRING(RawChargeNo, D3+1,
                               CASE WHEN D4 > 0 THEN D4 - D3 - 1 ELSE LEN(RawChargeNo) - D3 END)
                    FROM (
                        SELECT
                            D1 = CHARINDEX('-', RawChargeNo, 1),
                            D2 = CHARINDEX('-', RawChargeNo, CHARINDEX('-', RawChargeNo, 1) + 1),
                            D3 = CHARINDEX('-', RawChargeNo, CHARINDEX('-', RawChargeNo, CHARINDEX('-', RawChargeNo, 1) + 1) + 1),
                            D4 = CHARINDEX('-', RawChargeNo, CHARINDEX('-', RawChargeNo, CHARINDEX('-', RawChargeNo, CHARINDEX('-', RawChargeNo, 1) + 1) + 1) + 1)
                    ) d
                )
            END,
        FIRSTNAME,
        LASTNAME,
        ORGANIZNAME
    FROM #Clean;

    UPDATE #COA
    SET NaturalAccount = (
        SELECT RTRIM(SUBSTRING(C, D3+1,
                   CASE WHEN D4 > 0 THEN D4 - D3 - 1 ELSE LEN(C) - D3 END))
        FROM (SELECT C = RTRIM(Converted)) r
        CROSS APPLY (
            SELECT
                D1 = CHARINDEX('-', C, 1),
                D2 = CHARINDEX('-', C, CHARINDEX('-', C, 1) + 1),
                D3 = CHARINDEX('-', C, CHARINDEX('-', C, CHARINDEX('-', C, 1) + 1) + 1),
                D4 = CHARINDEX('-', C, CHARINDEX('-', C, CHARINDEX('-', C, CHARINDEX('-', C, 1) + 1) + 1) + 1)
        ) d
    )
    WHERE SegmentType IN ('PPM4', 'PPM6');

    /* ===== DAILY SLICE: find-or-create batch ===== */
    SET @AEJournalName =
        N'Equine sales ' +
        CONVERT(varchar(10), @currentStartDate, 120) + N' to ' +
        CONVERT(varchar(10), @currentEndDate, 120);

    SET @AEJournalReference =
        REPLACE(REPLACE(CONVERT(varchar(19), GETDATE(), 120), ':', '_'), ' ', '_');

    SELECT TOP (1)
        @batchID = b.batchID,
        @alreadySent = CASE WHEN b.dateSent IS NOT NULL THEN 1 ELSE 0 END
    FROM dbo.C_AE_Feed_Batch b
    WHERE b.WeekStartDate = @currentStartDate
      AND b.WeekEndDate   = @currentEndDate
    ORDER BY b.AETransactionDate DESC;

    IF @alreadySent = 1
    BEGIN
        /* Slice already sent to AE — do not reload */
        RETURN;
    END;

    BEGIN TRY
        BEGIN TRAN;

        IF @batchID IS NULL
        BEGIN
            SET @batchID = NEWID();

            INSERT INTO dbo.C_AE_Feed_Batch
            (
                batchID, dateSent, pickupSentFlag,
                AEConsumerReferenceID, AEConsumnerNotes,
                AERequestStatus, ErrorDetail, AEConsumerRequestID,
                AETransactionDate, AEJournalName, AEJournalDescription,
                AEJournalReference, batchTotal,
                WeekStartDate, WeekEndDate
            )
            VALUES
            (
                @batchID, NULL, 'A',
                CONCAT('GP_SALES_', CONVERT(char(6), @currentEndDate, 112)),
                N'Equine External Client Income',
                NULL, NULL, NULL,
                @currentEndDate,
                @AEJournalName, NULL, @AEJournalReference, NULL,
                @currentStartDate, @currentEndDate
            );
        END
        ELSE
        BEGIN
            /* Reuse batch: clear items for full reload of this slice */
            DELETE FROM dbo.C_AE_Feed_Items WHERE batchID = @batchID;

            UPDATE dbo.C_AE_Feed_Batch
            SET AEJournalReference = @AEJournalReference,
                AEJournalName      = @AEJournalName,
                AETransactionDate  = @currentEndDate,
                AERequestStatus    = NULL,
                ErrorDetail        = NULL,
                pickupSentFlag     = 'A',
                batchTotal         = NULL
            WHERE batchID = @batchID;
        END;

        /* Same SELECT pattern as validated CAHFS — HISTORY + OPEN; signed returns; VHFS501 */
        INSERT INTO dbo.C_AE_Feed_Items
        (
            orginalDocNumber,
            DebitChartString,
            CreditChartString,
            transactionDate,
            Testcode,
            TestName,
            quantity,
            unitPrice,
            totalCharge,
            batchID,
            clientID,
            DebitStringValid,
            CreditStringValid,
            Client_Name,
            Organization_Name,
            AccessionNumber
        )
        SELECT
            u.orginalDocNumber,
            u.DebitChartString,
            u.CreditChartString,
            u.transactionDate,
            u.TestCode,
            u.TestName,
            u.quantity,
            u.unitPrice,
            u.totalCharge,
            u.batchID,
            u.clientID,
            u.DebitStringValid,
            u.CreditStringValid,
            u.Client_Name,
            u.Organization_Name,
            u.AccessionNumber
        FROM
        (
            /* ===== EXTERNAL CLIENTS - HISTORY ===== */
            SELECT
                s303.SOPNUMBE AS orginalDocNumber,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN '3110-12107-VHFS501-410000-00-000-0000000000-000000-0000-000000-000000'
                     ELSE '3110-12107-VHFS501-127302-00-000-0000000000-000000-0000-000000-000000'
                END AS DebitChartString,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN '3110-12107-VHFS501-127302-00-000-0000000000-000000-0000-000000-000000'
                     ELSE '3110-12107-VHFS501-410000-00-000-0000000000-000000-0000-000000-000000'
                END AS CreditChartString,
                s302.POSTEDDT AS transactionDate,
                s303.ITEMNMBR AS TestCode,
                LEFT(s303.ITEMDESC,50) AS TestName,
                s303.QUANTITY AS quantity,
                s303.UNITPRCE AS unitPrice,
                CASE WHEN s302.SOPTYPE = 4 THEN -1 * s303.OXTNDPRC ELSE s303.OXTNDPRC END AS totalCharge,
                @batchID AS batchID,
                c.CUSTNMBR AS clientID,
                NULL AS DebitStringValid,
                NULL AS CreditStringValid,
                c.CUSTNAME AS Client_Name,
                NULL AS Organization_Name,
                NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
            FROM dbo.SOP30200 s302
            JOIN dbo.SOP30300 s303
                ON s303.SOPNUMBE = s302.SOPNUMBE
               AND s303.SOPTYPE  = s302.SOPTYPE
            JOIN dbo.RM00101 c
                ON c.CUSTNMBR = s302.CUSTNMBR
            LEFT JOIN dbo.SOP10106 udf
                ON udf.SOPTYPE  = s302.SOPTYPE
               AND udf.SOPNUMBE = s302.SOPNUMBE
            WHERE c.CUSTCLAS NOT IN ('I','U')
              AND s302.VOIDSTTS = 0
              AND s302.POSTEDDT >= @currentStartDate
              AND s302.POSTEDDT <  DATEADD(DAY,1,@currentEndDate)
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.SOP10100 OH2
                  WHERE OH2.SOPTYPE  = s302.SOPTYPE
                    AND OH2.SOPNUMBE = s302.SOPNUMBE
              )

            UNION ALL

            /* ===== EXTERNAL CLIENTS - OPEN ===== */
            SELECT
                s303.SOPNUMBE AS orginalDocNumber,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN '3110-12107-VHFS501-410000-00-000-0000000000-000000-0000-000000-000000'
                     ELSE '3110-12107-VHFS501-127302-00-000-0000000000-000000-0000-000000-000000'
                END AS DebitChartString,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN '3110-12107-VHFS501-127302-00-000-0000000000-000000-0000-000000-000000'
                     ELSE '3110-12107-VHFS501-410000-00-000-0000000000-000000-0000-000000-000000'
                END AS CreditChartString,
                s302.POSTEDDT AS transactionDate,
                s303.ITEMNMBR AS TestCode,
                LEFT(s303.ITEMDESC,50) AS TestName,
                s303.QUANTITY AS quantity,
                s303.UNITPRCE AS unitPrice,
                CASE WHEN s302.SOPTYPE = 4 THEN -1 * s303.OXTNDPRC ELSE s303.OXTNDPRC END AS totalCharge,
                @batchID AS batchID,
                c.CUSTNMBR AS clientID,
                NULL AS DebitStringValid,
                NULL AS CreditStringValid,
                c.CUSTNAME AS Client_Name,
                NULL AS Organization_Name,
                NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
            FROM dbo.SOP10100 s302
            JOIN dbo.SOP10200 s303
                ON s303.SOPNUMBE = s302.SOPNUMBE
               AND s303.SOPTYPE  = s302.SOPTYPE
            JOIN dbo.RM00101 c
                ON c.CUSTNMBR = s302.CUSTNMBR
            LEFT JOIN dbo.SOP10106 udf
                ON udf.SOPTYPE  = s302.SOPTYPE
               AND udf.SOPNUMBE = s302.SOPNUMBE
            WHERE c.CUSTCLAS NOT IN ('I','U')
              AND s302.VOIDSTTS = 0
              AND s302.POSTEDDT >= @currentStartDate
              AND s302.POSTEDDT <  DATEADD(DAY,1,@currentEndDate)

            UNION ALL

            /* ===== INTERNAL CLIENTS - HISTORY ===== */
            SELECT
                s303.SOPNUMBE AS orginalDocNumber,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN '3110-12107-VHFS501-775000-62-000-AR06660201-000000-0000-000000-000000'
                     ELSE NULL
                END AS DebitChartString,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN NULL
                     ELSE '3110-12107-VHFS501-775000-62-000-AR06660201-000000-0000-000000-000000'
                END AS CreditChartString,
                s302.POSTEDDT AS transactionDate,
                s303.ITEMNMBR AS TestCode,
                LEFT(s303.ITEMDESC,50) AS TestName,
                s303.QUANTITY AS quantity,
                s303.UNITPRCE AS unitPrice,
                CASE WHEN s302.SOPTYPE = 4 THEN -1 * s303.OXTNDPRC ELSE s303.OXTNDPRC END AS totalCharge,
                @batchID AS batchID,
                c.CUSTNMBR AS clientID,
                NULL AS DebitStringValid,
                NULL AS CreditStringValid,
                c.CUSTNAME AS Client_Name,
                NULL AS Organization_Name,
                NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
            FROM dbo.SOP30200 s302
            JOIN dbo.SOP30300 s303
                ON s303.SOPNUMBE = s302.SOPNUMBE
               AND s303.SOPTYPE  = s302.SOPTYPE
            JOIN dbo.RM00101 c
                ON c.CUSTNMBR = s302.CUSTNMBR
            LEFT JOIN dbo.SOP10106 udf
                ON udf.SOPTYPE  = s302.SOPTYPE
               AND udf.SOPNUMBE = s302.SOPNUMBE
            WHERE c.CUSTCLAS IN ('I','U')
              AND s302.VOIDSTTS = 0
              AND s302.POSTEDDT >= @currentStartDate
              AND s302.POSTEDDT <  DATEADD(DAY,1,@currentEndDate)
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.SOP10100 OH2
                  WHERE OH2.SOPTYPE  = s302.SOPTYPE
                    AND OH2.SOPNUMBE = s302.SOPNUMBE
              )

            UNION ALL

            /* ===== INTERNAL CLIENTS - OPEN ===== */
            SELECT
                s303.SOPNUMBE AS orginalDocNumber,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN '3110-12107-VHFS501-775000-62-000-AR06660201-000000-0000-000000-000000'
                     ELSE NULL
                END AS DebitChartString,
                CASE WHEN s303.OXTNDPRC < 0 OR s302.SOPTYPE = 4
                     THEN NULL
                     ELSE '3110-12107-VHFS501-775000-62-000-AR06660201-000000-0000-000000-000000'
                END AS CreditChartString,
                s302.POSTEDDT AS transactionDate,
                s303.ITEMNMBR AS TestCode,
                LEFT(s303.ITEMDESC,50) AS TestName,
                s303.QUANTITY AS quantity,
                s303.UNITPRCE AS unitPrice,
                CASE WHEN s302.SOPTYPE = 4 THEN -1 * s303.OXTNDPRC ELSE s303.OXTNDPRC END AS totalCharge,
                @batchID AS batchID,
                c.CUSTNMBR AS clientID,
                NULL AS DebitStringValid,
                NULL AS CreditStringValid,
                c.CUSTNAME AS Client_Name,
                NULL AS Organization_Name,
                NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
            FROM dbo.SOP10100 s302
            JOIN dbo.SOP10200 s303
                ON s303.SOPNUMBE = s302.SOPNUMBE
               AND s303.SOPTYPE  = s302.SOPTYPE
            JOIN dbo.RM00101 c
                ON c.CUSTNMBR = s302.CUSTNMBR
            LEFT JOIN dbo.SOP10106 udf
                ON udf.SOPTYPE  = s302.SOPTYPE
               AND udf.SOPNUMBE = s302.SOPNUMBE
            WHERE c.CUSTCLAS IN ('I','U')
              AND s302.VOIDSTTS = 0
              AND s302.POSTEDDT >= @currentStartDate
              AND s302.POSTEDDT <  DATEADD(DAY,1,@currentEndDate)
        ) u;

        /* APPLY STARLIMS CHARGENO COA TO INTERNAL CLIENTS */
        UPDATE i
        SET
            i.CreditChartString =
                CASE
                    WHEN i.CreditChartString IS NULL
                         AND c.SegmentType IN ('INVALID', 'GL_REVIEW', 'PPM_REVIEW')
                        THEN 'ERROR'
                    WHEN i.CreditChartString IS NULL
                         AND c.SegmentType NOT IN ('INVALID', 'GL_REVIEW', 'PPM_REVIEW')
                        THEN c.Converted
                    ELSE i.CreditChartString
                END,
            i.DebitChartString =
                CASE
                    WHEN i.DebitChartString IS NULL AND c.SegmentType IS NOT NULL
                        THEN
                            CASE
                                WHEN c.NaturalAccount LIKE '77%' OR c.NaturalAccount IS NULL
                                    THEN c.Converted
                                ELSE (
                                    SELECT
                                        LEFT(c.Converted, D3) + '770003' +
                                        CASE WHEN D4 > 0
                                             THEN SUBSTRING(c.Converted, D4, LEN(c.Converted))
                                             ELSE ''
                                        END
                                    FROM (
                                        SELECT
                                            D1 = CHARINDEX('-', c.Converted, 1),
                                            D2 = CHARINDEX('-', c.Converted, CHARINDEX('-', c.Converted, 1) + 1),
                                            D3 = CHARINDEX('-', c.Converted, CHARINDEX('-', c.Converted, CHARINDEX('-', c.Converted, 1) + 1) + 1),
                                            D4 = CHARINDEX('-', c.Converted, CHARINDEX('-', c.Converted, CHARINDEX('-', c.Converted, CHARINDEX('-', c.Converted, 1) + 1) + 1) + 1)
                                    ) d
                                )
                            END
                    ELSE i.DebitChartString
                END,
            i.CreditStringValid =
                CASE
                    WHEN i.CreditStringValid = 'ERROR' THEN 'ERROR'
                    WHEN c.SegmentType IN ('GL8', 'GL11', 'GLPPM9', 'GL_REVIEW')
                         AND c.Converted LIKE '3310%' AND i.CreditChartString IS NULL
                        THEN 'ERROR - ANR Interlocation Office Charge'
                    ELSE i.CreditStringValid
                END,
            i.DebitStringValid =
                CASE
                    WHEN i.DebitStringValid = 'ERROR' THEN 'ERROR'
                    WHEN c.SegmentType IN ('GL8', 'GL11', 'GLPPM9', 'GL_REVIEW')
                         AND c.Converted LIKE '3310%' AND i.DebitChartString IS NULL
                        THEN 'ERROR - ANR Interlocation Office Charge'
                    ELSE i.DebitStringValid
                END,
            i.UnprocessedCOAString =
                CASE
                    WHEN c.RawChargeNo IS NULL AND i.DebitChartString IS NULL
                        THEN 'Predicted NULL from StarLIMS'
                    WHEN c.RawChargeNo IS NULL AND i.CreditChartString IS NULL
                        THEN 'Predicted NULL from StarLIMS'
                    ELSE c.RawChargeNo
                END,
            i.Organization_Name = c.ORGANIZNAME
        FROM dbo.C_AE_Feed_Items i
        LEFT JOIN #COA c ON c.DOCNUMBER = i.orginalDocNumber
        JOIN dbo.RM00101 cl ON cl.CUSTNMBR = i.clientID
        WHERE i.batchID = @batchID
          AND cl.CUSTCLAS IN ('I', 'U');

        UPDATE b
        SET b.batchTotal = x.sum_amount,
            b.pickupSentFlag = CASE WHEN x.sum_amount IS NULL THEN b.pickupSentFlag ELSE 'P' END
        FROM dbo.C_AE_Feed_Batch b
        OUTER APPLY (
            SELECT SUM(i.totalCharge) AS sum_amount
            FROM dbo.C_AE_Feed_Items i
            WHERE i.batchID = b.batchID
        ) x
        WHERE b.batchID = @batchID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRAN;

        IF @batchID IS NOT NULL
        BEGIN
            UPDATE dbo.C_AE_Feed_Batch
            SET AERequestStatus = 'ERROR',
                ErrorDetail = CONCAT('Load failed: ', ERROR_MESSAGE())
            WHERE batchID = @batchID;
        END;

        THROW;
    END CATCH;
END;
GO
