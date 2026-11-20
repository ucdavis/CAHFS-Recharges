# Equine (`gpare`) AE feed load updates

Deploy **on `gpare` only** (not `gparc` / CAHFS).

## Changes

1. **VHFS501** chart strings (was VHFS001)
2. **Returns (`SOPTYPE = 4`)** use reverse COA (same as negative `OXTNDPRC`); amount stays `ABS`
3. **AccessionNumber** from `SOP10106.USERDEF1` into `C_AE_Feed_Items`

## Deploy order

1. Run `001_C_AE_Feed_Items_AccessionNumber.sql` on **gpare** (and on **gparc** before app deploy — same column, CAHFS proc can fill it later)
2. Open `gpare.dbo.C_Load_AE_Feed_Tables_Weekly_V2` in SSMS
3. Replace the `INSERT INTO dbo.C_AE_Feed_Items ... ) u;` block with the contents of `002_C_Load_AE_Feed_Tables_Weekly_V2_INSERT_block.sql`
4. Save / `ALTER` the procedure
5. Clear or rebuild Equine test batches for the weeks under test, then re-run the proc
6. Deploy the CAEI app build that maps `AccessionNumber` → Glide `UdfString3` and `ClientID` → `UdfString4`

## Smoke checks after reload

```sql
-- Chart department
SELECT TOP 20 DebitChartString, CreditChartString, orginalDocNumber, AccessionNumber, totalCharge
FROM dbo.C_AE_Feed_Items
ORDER BY transactionDate DESC;

-- Joe's return (example)
SELECT orginalDocNumber, AccessionNumber, DebitChartString, CreditChartString, totalCharge, transactionDate
FROM dbo.C_AE_Feed_Items
WHERE orginalDocNumber = '00045'
   OR AccessionNumber = 'D2310704';
```

Expect VHFS501 in chart strings, AccessionNumber = D2310704 for return 00045, and swapped AR/income vs a normal invoice.
