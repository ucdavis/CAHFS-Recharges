/*
  BillingId → RM00101.CUSTNMBR resolution (CAHFS / EQUINE company DBs).

  Rules:
  1) Trim leading/trailing spaces only. Do NOT strip leading zeros.
  2) Look up RTRIM(CUSTNMBR) = trimmed BillingId.
  3) If not found and LEN(trimmed) < 8, left-pad with '0' to length 8 and look up again.
  4) When a match is found, usp_C_LB_Normalize_BillingIds persists that CUSTNMBR
     back onto C_LB_Payment_Staging.BillingId.

  Run BEFORE 002_C_LB_Pending_Cash_Receipts_V.sql (view depends on the function).
  Run on BOTH gparc and gpare.
*/

SET NOCOUNT ON;
GO

CREATE OR ALTER FUNCTION dbo.fn_C_LB_ResolveCustNmbr (@BillingId varchar(50))
RETURNS varchar(15)
AS
BEGIN
    DECLARE @candidate varchar(50) = LTRIM(RTRIM(ISNULL(@BillingId, '')));
    DECLARE @matched varchar(15) = NULL;
    DECLARE @padded varchar(15);

    -- Leading zeros are significant — never strip them; only spaces were removed above.
    IF @candidate = N''
        RETURN NULL;

    -- 1) Exact match after space-trim
    SELECT TOP (1) @matched = RTRIM(c.CUSTNMBR)
    FROM dbo.RM00101 AS c
    WHERE RTRIM(c.CUSTNMBR) = @candidate
      AND LEN(LTRIM(RTRIM(c.CUSTNMBR))) > 0
    ORDER BY c.CUSTNMBR;

    IF @matched IS NOT NULL
        RETURN @matched;

    -- 2) Pad with leading zeros to 8 characters and retry
    IF LEN(@candidate) > 0 AND LEN(@candidate) < 8
    BEGIN
        SET @padded = RIGHT(REPLICATE('0', 8) + @candidate, 8);

        SELECT TOP (1) @matched = RTRIM(c.CUSTNMBR)
        FROM dbo.RM00101 AS c
        WHERE RTRIM(c.CUSTNMBR) = @padded
          AND LEN(LTRIM(RTRIM(c.CUSTNMBR))) > 0
        ORDER BY c.CUSTNMBR;

        IF @matched IS NOT NULL
            RETURN @matched;
    END

    RETURN NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_C_LB_Normalize_BillingIds
AS
BEGIN
    SET NOCOUNT ON;

    /*
      Persist resolved CUSTNMBR onto staging so BillingId matches GP
      (and eConnect) for later runs / UI.
    */
    UPDATE s
    SET s.BillingId = r.CustNmbr
    FROM dbo.C_LB_Payment_Staging AS s
    CROSS APPLY (SELECT dbo.fn_C_LB_ResolveCustNmbr(s.BillingId) AS CustNmbr) AS r
    WHERE r.CustNmbr IS NOT NULL
      AND LTRIM(RTRIM(ISNULL(s.BillingId, ''))) <> r.CustNmbr
      AND s.ValidationStatus = 'VALID'
      AND ISNULL(s.PostStatus, '') <> 'Posted';

    SELECT @@ROWCOUNT AS BillingIdsUpdated;
END;
GO
