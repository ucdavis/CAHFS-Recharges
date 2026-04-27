# CAHFS-Recharges

## StarLIMS connection (Dev/Test/Prod)

- **Connection string key**: `ConnectionStrings:StarLIMSDB`
- **Where it’s used**:
  - Read access: `CAHFS Recharges/Data/StarLIMSContext.cs`
  - COA write-back (when correcting COA in CAEI): updates `dbo.C_GP_INVOICES.CHARGENO`, `dbo.INVOICES.CHARGENO`, and `dbo.folder_roles.ChargeNo` in the StarLIMS DB for the **current environment** (the database selected by the `StarLIMSDB` connection string).
- **Environment**: set `ASPNETCORE_ENVIRONMENT` and provide the matching `appsettings.{Environment}.json` / env var overrides so `StarLIMSDB` points to the intended **Development/Test/Production** StarLIMS database.