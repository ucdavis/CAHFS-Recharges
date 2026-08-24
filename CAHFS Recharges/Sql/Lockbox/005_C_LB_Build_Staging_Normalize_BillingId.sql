/*
  Patch: Normalize BillingId inside dbo.C_LB_Build_Staging

  Goal:
  - Trim spaces around BillingId (keep leading zeros)
  - If not found in RM00101 and length < 8, left-pad with zeros to 8 and retry
  - If a match is found, update C_LB_Payment_Staging.BillingId to the GP CUSTNMBR

  Run on BOTH company DBs:
    - gparc (CAHFS)
    - gpare (EQUINE)

  Note:
  - BillingId normalize + INVALID_CUSTOMER status after Step 3.
  - Does not overwrite trailer ERROR or no-addenda WARN.
*/

SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[C_LB_Build_Staging]
    @FileName VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    -- STEP 1: INSERT staging rows (REC6 + REC4, and REC6 without REC4)
    INSERT INTO dbo.C_LB_Payment_Staging (
        FileName, Currency, TransactionType,
        LockboxNumber, DepositDate,
        BatchNumber, SeqNumber,
        BankNumber, AccountNumber,
        CheckNumber, RemitterName,
        CheckAmount,
        AddendaSeq, AddendaType,
        BillingId, EnvFlag,
        AccessionFull,
        IsPrimaryAddenda, HasAddenda, AddendaCount,
        ValidationStatus,
        PaymentDetailId, PaymentAddendaId
    )
    -- Branch A: checks with addenda
    SELECT
        LEFT(r6.FileName, 200),
        'USD',
        'Check',
        RTRIM(b5.LockboxNumber),
        b5.DepositDate,
        LEFT(RTRIM(r6.BatchNumber), 3),
        TRY_CAST(r6.ItemSeqNumber AS INT),
        r6.BankRouting,
        LEFT(RTRIM(r6.AccountNumber), 20),
        LEFT(RTRIM(r6.CheckNumber), 20),
        LEFT(RTRIM(r6.RemitterName), 200),
        r6.CheckAmount,
        r4.SeqInstance,
        NULLIF(RTRIM(r4.AddendaType), ''),
        r4.BillingIdHex,
        NULLIF(r4.EnvFlag, '00'),
        r4.AccessionRaw,
        CASE WHEN r4.SeqInstance = 1 THEN 1 ELSE 0 END,
        1,
        ac.cnt,
        'PENDING',
        r6.PaymentDetailId,
        r4.PaymentAddendaId
    FROM dbo.C_LB_Payment_Detail r6
    LEFT JOIN dbo.C_LB_Batch_Header b5
           ON b5.FileName = r6.FileName
    INNER JOIN dbo.C_LB_Payment_Addenda r4
            ON r4.BatchNumber    = r6.BatchNumber
           AND r4.ItemSeqNumber = r6.ItemSeqNumber
           AND r4.FileName       = r6.FileName
    JOIN (
        SELECT FileName, BatchNumber, ItemSeqNumber,
               COUNT(*) AS cnt
        FROM dbo.C_LB_Payment_Addenda
        WHERE FileName = @FileName
        GROUP BY FileName, BatchNumber, ItemSeqNumber
    ) ac
      ON ac.FileName       = r6.FileName
     AND ac.BatchNumber    = r6.BatchNumber
     AND ac.ItemSeqNumber  = r6.ItemSeqNumber
    WHERE r6.FileName = @FileName

    UNION ALL

    -- Branch B: checks with no addenda
    SELECT
        LEFT(r6.FileName, 200),
        'USD',
        'Check',
        RTRIM(b5.LockboxNumber),
        b5.DepositDate,
        LEFT(RTRIM(r6.BatchNumber), 3),
        TRY_CAST(r6.ItemSeqNumber AS INT),
        r6.BankRouting,
        LEFT(RTRIM(r6.AccountNumber), 20),
        LEFT(RTRIM(r6.CheckNumber), 20),
        LEFT(RTRIM(r6.RemitterName), 200),
        r6.CheckAmount,
        0,
        NULL,
        NULL,
        NULL,
        NULL,
        1,
        0,
        0,
        'PENDING',
        r6.PaymentDetailId,
        NULL
    FROM dbo.C_LB_Payment_Detail r6
    LEFT JOIN dbo.C_LB_Batch_Header b5
           ON b5.FileName = r6.FileName
    WHERE r6.FileName = @FileName
      AND NOT EXISTS (
            SELECT 1
            FROM dbo.C_LB_Payment_Addenda r4x
            WHERE r4x.FileName       = r6.FileName
              AND r4x.BatchNumber    = r6.BatchNumber
              AND r4x.ItemSeqNumber = r6.ItemSeqNumber
          );

    -- STEP 2: VALIDATION vs batch trailer
    UPDATE s
    SET
        s.ValidationStatus = CASE
            WHEN t.BatchTotalAmt = sums.total_sum
             AND t.ItemCount      = sums.item_cnt
            THEN 'VALID'
            ELSE 'ERROR'
        END,
        s.ValidationNotes = LEFT(CASE
            WHEN t.BatchTotalAmt <> sums.total_sum
            THEN 'Amount mismatch: trailer=$'
                 + CAST(t.BatchTotalAmt AS VARCHAR(20))
                 + ' | actual=$'
                 + CAST(sums.total_sum AS VARCHAR(20))
            WHEN t.ItemCount <> sums.item_cnt
            THEN 'Count mismatch: trailer='
                 + CAST(t.ItemCount AS VARCHAR(10))
                 + ' | actual='
                 + CAST(sums.item_cnt AS VARCHAR(10))
            ELSE NULL
        END, 500)
    FROM dbo.C_LB_Payment_Staging s
    JOIN dbo.C_LB_Batch_Trailer t
      ON TRY_CAST(t.BatchNumber AS INT) = TRY_CAST(s.BatchNumber AS INT)
     AND t.FileName = s.FileName
    JOIN (
        SELECT
            BatchNumber,
            FileName,
            SUM(CheckAmount) AS total_sum,
            COUNT(*)         AS item_cnt
        FROM dbo.C_LB_Payment_Staging
        WHERE FileName = @FileName
          AND IsPrimaryAddenda = 1
        GROUP BY BatchNumber, FileName
    ) sums
      ON sums.BatchNumber = s.BatchNumber
     AND sums.FileName    = s.FileName
    WHERE s.FileName = @FileName;

    -- STEP 3: WARN when no addenda (trailer step may already set VALID — still warn)
    UPDATE dbo.C_LB_Payment_Staging
    SET
        ValidationStatus = 'WARN',
        ValidationNotes  = 'No addenda record — manual billing reference matching required'
    WHERE FileName = @FileName
      AND HasAddenda = 0
      AND ValidationStatus IN ('PENDING', 'VALID');

    /*
      STEP 3b: Normalize BillingId against RM00101, then flag unmatched customers.

      - Trim spaces (keep leading zeros)
      - If no match and LEN < 8, left-pad with zeros to 8 and retry
      - If match found, overwrite staging BillingId with the GP CUSTNMBR
      - Blank BillingId OR no RM00101 match → INVALID_CUSTOMER
      - Does not overwrite trailer ERROR or no-addenda WARN
    */
    ;WITH Candidates AS (
        SELECT
            s.StagingId,
            LTRIM(RTRIM(ISNULL(s.BillingId, ''))) AS TrimmedId,
            CASE
                WHEN LEN(LTRIM(RTRIM(ISNULL(s.BillingId, '')))) BETWEEN 1 AND 7
                THEN RIGHT(REPLICATE('0', 8) + LTRIM(RTRIM(ISNULL(s.BillingId, ''))), 8)
                ELSE NULL
            END AS PaddedId
        FROM dbo.C_LB_Payment_Staging s
        WHERE s.FileName = @FileName
          AND s.HasAddenda = 1
          AND ISNULL(s.PostStatus, '') <> 'Posted'
          AND LEN(LTRIM(RTRIM(ISNULL(s.BillingId, '')))) > 0
    ),
    Matches AS (
        SELECT
            c.StagingId,
            COALESCE(m1.CustNmbr, m2.CustNmbr) AS ResolvedCustNmbr
        FROM Candidates c
        OUTER APPLY (
            SELECT TOP (1) RTRIM(rm.CUSTNMBR) AS CustNmbr
            FROM dbo.RM00101 rm
            WHERE LEN(LTRIM(RTRIM(rm.CUSTNMBR))) > 0
              AND RTRIM(rm.CUSTNMBR) = c.TrimmedId
        ) m1
        OUTER APPLY (
            SELECT TOP (1) RTRIM(rm.CUSTNMBR) AS CustNmbr
            FROM dbo.RM00101 rm
            WHERE c.PaddedId IS NOT NULL
              AND LEN(LTRIM(RTRIM(rm.CUSTNMBR))) > 0
              AND RTRIM(rm.CUSTNMBR) = c.PaddedId
        ) m2
        WHERE COALESCE(m1.CustNmbr, m2.CustNmbr) IS NOT NULL
    )
    UPDATE s
    SET s.BillingId = m.ResolvedCustNmbr
    FROM dbo.C_LB_Payment_Staging s
    INNER JOIN Matches m
        ON m.StagingId = s.StagingId
    WHERE s.FileName = @FileName
      AND LTRIM(RTRIM(ISNULL(s.BillingId, ''))) <> m.ResolvedCustNmbr;

    -- Blank BillingId with addenda → always INVALID_CUSTOMER (do not rely on RM join;
    -- blank CUSTNMBR rows in RM00101 can falsely "match" '')
    UPDATE dbo.C_LB_Payment_Staging
    SET
        ValidationStatus = 'INVALID_CUSTOMER',
        ValidationNotes  = 'Billing ID is blank — enter a valid GP customer (CUSTNMBR) before posting.'
    WHERE FileName = @FileName
      AND HasAddenda = 1
      AND ISNULL(PostStatus, '') <> 'Posted'
      AND ValidationStatus IN ('VALID', 'PENDING', 'INVALID_CUSTOMER')
      AND LTRIM(RTRIM(ISNULL(BillingId, ''))) = '';

    -- Non-blank BillingId that still does not match a real (non-blank) RM customer
    UPDATE s
    SET
        s.ValidationStatus = 'INVALID_CUSTOMER',
        s.ValidationNotes = LEFT(
            'Customer Number (CUSTNMBR) "'
            + LTRIM(RTRIM(s.BillingId))
            + '" was not found in RM00101. Correct Billing ID before GP posting.',
            500)
    FROM dbo.C_LB_Payment_Staging s
    WHERE s.FileName = @FileName
      AND s.HasAddenda = 1
      AND ISNULL(s.PostStatus, '') <> 'Posted'
      AND s.ValidationStatus IN ('VALID', 'PENDING', 'INVALID_CUSTOMER')
      AND LTRIM(RTRIM(ISNULL(s.BillingId, ''))) <> ''
      AND NOT EXISTS (
            SELECT 1
            FROM dbo.RM00101 rm
            WHERE LEN(LTRIM(RTRIM(rm.CUSTNMBR))) > 0
              AND RTRIM(rm.CUSTNMBR) = LTRIM(RTRIM(s.BillingId))
        );

    -- STEP 4: Summary
    SELECT
        ValidationStatus AS [Status],
        COUNT(DISTINCT CONCAT(BatchNumber, '|', CAST(SeqNumber AS VARCHAR(10)))) AS [Unique Checks],
        COUNT(*) AS [Staging Rows],
        SUM(CASE WHEN IsPrimaryAddenda = 1 THEN CheckAmount ELSE 0 END) AS [Total Amount]
    FROM dbo.C_LB_Payment_Staging
    WHERE FileName = @FileName
    GROUP BY ValidationStatus
    ORDER BY ValidationStatus;
END;
GO

