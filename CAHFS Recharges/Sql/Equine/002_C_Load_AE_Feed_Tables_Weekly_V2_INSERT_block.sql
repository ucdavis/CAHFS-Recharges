/*
  Equine (gpare) — replacement INSERT block for dbo.C_Load_AE_Feed_Tables_Weekly_V2

  Changes vs current gpare proc:
  1) Chart strings: VHFS001 -> VHFS501 (external AR/income + internal placeholder)
  2) Returns: treat SOPTYPE = 4 like OXTNDPRC < 0 (COA swap); keep ABS(totalCharge)
  3) Accession: LEFT JOIN SOP10106, AccessionNumber = RTRIM(USERDEF1)

  Prerequisites:
  - Run Sql/Equine/001_C_AE_Feed_Items_AccessionNumber.sql on gpare
  - Paste this block over the existing INSERT INTO dbo.C_AE_Feed_Items ... ) u;
    inside the weekly WHILE (leave StarLIMS #COA steps unchanged)

  Do NOT apply VHFS501 to gparc (CAHFS).
*/

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
                    s302.GLPOSTDT AS transactionDate,
                    s303.ITEMNMBR AS TestCode,
                    LEFT(s303.ITEMDESC,50) AS TestName,
                    s303.QUANTITY AS quantity,
                    s303.UNITPRCE AS unitPrice,
                    ABS(s303.OXTNDPRC) AS totalCharge,
                    @batchID AS batchID,
                    c.CUSTNMBR AS clientID,
                    NULL AS DebitStringValid,
                    NULL AS CreditStringValid,
                    c.CUSTNAME AS Client_Name,
                    NULL AS Organization_Name,
                    NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
                FROM SOP30200 s302
                JOIN SOP30300 s303
                    ON s303.SOPNUMBE = s302.SOPNUMBE
                   AND s303.SOPTYPE  = s302.SOPTYPE
                JOIN RM00101 c
                    ON c.CUSTNMBR = s302.CUSTNMBR
                LEFT JOIN SOP10106 udf
                    ON udf.SOPTYPE  = s302.SOPTYPE
                   AND udf.SOPNUMBE = s302.SOPNUMBE
                WHERE c.CUSTCLAS NOT IN ('I','U')
                  AND s302.VOIDSTTS = 0
                  AND s302.GLPOSTDT >= @currentStartDate
                  AND s302.GLPOSTDT <  DATEADD(DAY,1,@currentEndDate)
                  AND NOT EXISTS (
                      SELECT 1 FROM SOP10100 OH2
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
                    s302.GLPOSTDT AS transactionDate,
                    s303.ITEMNMBR AS TestCode,
                    LEFT(s303.ITEMDESC,50) AS TestName,
                    s303.QUANTITY AS quantity,
                    s303.UNITPRCE AS unitPrice,
                    ABS(s303.OXTNDPRC) AS totalCharge,
                    @batchID AS batchID,
                    c.CUSTNMBR AS clientID,
                    NULL AS DebitStringValid,
                    NULL AS CreditStringValid,
                    c.CUSTNAME AS Client_Name,
                    NULL AS Organization_Name,
                    NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
                FROM SOP10100 s302
                JOIN SOP10200 s303
                    ON s303.SOPNUMBE = s302.SOPNUMBE
                   AND s303.SOPTYPE  = s302.SOPTYPE
                JOIN RM00101 c
                    ON c.CUSTNMBR = s302.CUSTNMBR
                LEFT JOIN SOP10106 udf
                    ON udf.SOPTYPE  = s302.SOPTYPE
                   AND udf.SOPNUMBE = s302.SOPNUMBE
                WHERE c.CUSTCLAS NOT IN ('I','U')
                  AND s302.VOIDSTTS = 0
                  AND s302.GLPOSTDT >= @currentStartDate
                  AND s302.GLPOSTDT <  DATEADD(DAY,1,@currentEndDate)

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
                    s302.GLPOSTDT AS transactionDate,
                    s303.ITEMNMBR AS TestCode,
                    LEFT(s303.ITEMDESC,50) AS TestName,
                    s303.QUANTITY AS quantity,
                    s303.UNITPRCE AS unitPrice,
                    ABS(s303.OXTNDPRC) AS totalCharge,
                    @batchID AS batchID,
                    c.CUSTNMBR AS clientID,
                    NULL AS DebitStringValid,
                    NULL AS CreditStringValid,
                    c.CUSTNAME AS Client_Name,
                    NULL AS Organization_Name,
                    NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
                FROM SOP30200 s302
                JOIN SOP30300 s303
                    ON s303.SOPNUMBE = s302.SOPNUMBE
                   AND s303.SOPTYPE  = s302.SOPTYPE
                JOIN RM00101 c
                    ON c.CUSTNMBR = s302.CUSTNMBR
                LEFT JOIN SOP10106 udf
                    ON udf.SOPTYPE  = s302.SOPTYPE
                   AND udf.SOPNUMBE = s302.SOPNUMBE
                WHERE c.CUSTCLAS IN ('I','U')
                  AND s302.VOIDSTTS = 0
                  AND s302.GLPOSTDT >= @currentStartDate
                  AND s302.GLPOSTDT <  DATEADD(DAY,1,@currentEndDate)
                  AND NOT EXISTS (
                      SELECT 1 FROM SOP10100 OH2
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
                    s302.GLPOSTDT AS transactionDate,
                    s303.ITEMNMBR AS TestCode,
                    LEFT(s303.ITEMDESC,50) AS TestName,
                    s303.QUANTITY AS quantity,
                    s303.UNITPRCE AS unitPrice,
                    ABS(s303.OXTNDPRC) AS totalCharge,
                    @batchID AS batchID,
                    c.CUSTNMBR AS clientID,
                    NULL AS DebitStringValid,
                    NULL AS CreditStringValid,
                    c.CUSTNAME AS Client_Name,
                    NULL AS Organization_Name,
                    NULLIF(RTRIM(udf.USERDEF1), '') AS AccessionNumber
                FROM SOP10100 s302
                JOIN SOP10200 s303
                    ON s303.SOPNUMBE = s302.SOPNUMBE
                   AND s303.SOPTYPE  = s302.SOPTYPE
                JOIN RM00101 c
                    ON c.CUSTNMBR = s302.CUSTNMBR
                LEFT JOIN SOP10106 udf
                    ON udf.SOPTYPE  = s302.SOPTYPE
                   AND udf.SOPNUMBE = s302.SOPNUMBE
                WHERE c.CUSTCLAS IN ('I','U')
                  AND s302.VOIDSTTS = 0
                  AND s302.GLPOSTDT >= @currentStartDate
                  AND s302.GLPOSTDT <  DATEADD(DAY,1,@currentEndDate)
            ) u;
