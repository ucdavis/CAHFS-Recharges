# CAHFS (`gparc`) AE feed load updates

Deploy **on `gparc` only**. Keep **VHFS001** (do not use Equine VHFS501).

## Changes

1. **Returns (`SOPTYPE = 4`)** use reverse COA (same as negative `OXTNDPRC`); amount stays `ABS`
2. **AccessionNumber** from `SOP10106.USERDEF1` into `C_AE_Feed_Items`
3. Chart strings remain **VHFS001**

## Deploy order

1. Run `001_C_AE_Feed_Items_AccessionNumber.sql` on **gparc**
2. Open `gparc.dbo.C_Load_AE_Feed_Tables_Weekly_V2` in SSMS
3. Replace the `INSERT INTO dbo.C_AE_Feed_Items ... ) u;` block with `002_C_Load_AE_Feed_Tables_Weekly_V2_INSERT_block.sql`
4. Save / `ALTER` the procedure
5. Rebuild CAHFS test batches for weeks under test, then re-run the proc
6. Deploy the CAEI app build (`AccessionNumber` → Glide `UdfString3`, `ClientID` → `UdfString4`)

## Smoke check — Joe's return

```sql
SELECT orginalDocNumber, AccessionNumber, DebitChartString, CreditChartString, totalCharge, transactionDate
FROM dbo.C_AE_Feed_Items
WHERE orginalDocNumber = '00045'
   OR AccessionNumber = 'D2310704';
```

Expect AccessionNumber = `D2310704`, VHFS001 chart strings, and swapped AR/income vs a normal invoice.
