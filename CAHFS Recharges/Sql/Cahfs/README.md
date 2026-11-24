# CAHFS (`gparc`) AE feed load

## Daily month-slice batches (current)

Proc: `dbo.C_Load_AE_Feed_Tables_Weekly_V2` (see `004_C_Load_AE_Feed_Tables_Weekly_V2_Daily.sql`)

| Slice | Days |
|-------|------|
| 1 | 1–7 |
| 2 | 8–14 |
| 3 | 15–21 |
| 4 | 22–28 |
| 5 | 29–EOM (e.g. Nov 29–30) |

- Job runs **daily**
- First run in a slice **creates** the batch; later days **reload** into the same batch
- Selection + line `transactionDate` = **Posted date (`POSTEDDT`)**
- **HISTORY + OPEN** (validated CAHFS SELECT — do not change)
- Returns (`SOPTYPE = 4` / negative): reverse COA + **positive** `ABS(amount)` (AE NonNegativeFloat)
- Accession = `SOP10106.USERDEF1`
- If `dateSent` is set, slice is skipped (already sent)

Backfill example:

```sql
EXEC dbo.C_Load_AE_Feed_Tables_Weekly_V2 @AsOfDate = '2026-11-03';  -- Nov 1–7 slice
EXEC dbo.C_Load_AE_Feed_Tables_Weekly_V2 @AsOfDate = '2026-11-29'; -- Nov 29–30 slice
```

## Deploy order on `gparc`

1. `001_C_AE_Feed_Items_AccessionNumber.sql`
2. `003_C_AE_Feed_Batch_WeekDates.sql`
3. `004_C_Load_AE_Feed_Tables_Weekly_V2_Daily.sql` (CREATE OR ALTER)
4. `005_SQLAgent_CAEI_CAHFS_AE_Feed_Daily.sql` (SQL Agent job — 1:00 AM local; change job owner from `sa`)

`002_…INSERT_block.sql` is superseded by `004` for full daily behavior (kept for reference).

### Prod schedule (Pacific / server local)

| Time | What |
|------|------|
| 1:00 AM | This Agent job — load current month-slice |
| 2:00 AM | Hangfire `DailyCoaValidation` (app) |
| Manual | AE send in CAEI UI |

## Backfill (no proc change) — May → today

```sql
USE [gparc];
GO

DECLARE @from date = '2026-05-01';
DECLARE @to   date = CAST(GETDATE() AS date);
DECLARE @d    date = @from;
DECLARE @sliceEnd date;

WHILE @d <= @to
BEGIN
    SET @sliceEnd =
        CASE
            WHEN DAY(@d) BETWEEN 1  AND 7  THEN DATEFROMPARTS(YEAR(@d), MONTH(@d), 7)
            WHEN DAY(@d) BETWEEN 8  AND 14 THEN DATEFROMPARTS(YEAR(@d), MONTH(@d), 14)
            WHEN DAY(@d) BETWEEN 15 AND 21 THEN DATEFROMPARTS(YEAR(@d), MONTH(@d), 21)
            WHEN DAY(@d) BETWEEN 22 AND 28 THEN DATEFROMPARTS(YEAR(@d), MONTH(@d), 28)
            ELSE EOMONTH(@d)
        END;

    IF @sliceEnd > @to SET @sliceEnd = @to;

    PRINT CONCAT('CAHFS slice as-of ', CONVERT(char(10), @sliceEnd, 120));
    EXEC dbo.C_Load_AE_Feed_Tables_Weekly_V2 @AsOfDate = @sliceEnd;

    SET @d =
        CASE
            WHEN DAY(@sliceEnd) = 7  THEN DATEADD(DAY, 1, @sliceEnd)
            WHEN DAY(@sliceEnd) = 14 THEN DATEADD(DAY, 1, @sliceEnd)
            WHEN DAY(@sliceEnd) = 21 THEN DATEADD(DAY, 1, @sliceEnd)
            WHEN DAY(@sliceEnd) = 28 THEN DATEADD(DAY, 1, @sliceEnd)
            ELSE DATEADD(DAY, 1, EOMONTH(@sliceEnd))
        END;
END;
GO
```

## Smoke checks

```sql
SELECT batchID, WeekStartDate, WeekEndDate, AEJournalName, batchTotal, dateSent, AERequestStatus
FROM dbo.C_AE_Feed_Batch
ORDER BY WeekStartDate DESC;

SELECT orginalDocNumber, AccessionNumber, transactionDate, totalCharge, DebitChartString, CreditChartString
FROM dbo.C_AE_Feed_Items
WHERE orginalDocNumber = '00045' OR AccessionNumber = 'D2310704';
```
