# LockboxCashReceiptPoster

Scheduled console worker that posts pending lockbox cash receipts from `C_LB_Payment_Staging` into Dynamics GP 18 via eConnect (IM replacement). Destination is behind `ICashReceiptPoster` so TraceFirst can replace GP later without changing the run loop.

## Why this is a separate .NET Framework 4.8 console (not inside CAEI net9)

| Reason | Detail |
|---|---|
| **eConnect compatibility** | GP 18 eConnect DLLs are .NET Framework assemblies. Loading them in the net9 Razor web app is fragile and unsupported. |
| **Host next to ERP** | eConnect Integration Service runs on the GP box. The poster must run there (Task Scheduler), not inside IIS/CAEI. |
| **Separation of concerns** | CAEI already owns SFTP → parse → staging. Posting is a separate integration worker (industry-standard IM replacement pattern). |
| **TraceFirst-ready** | Swap `GpEConnectCashReceiptPoster` for a TraceFirst implementation of `ICashReceiptPoster`; keep source/status/loop. |

## Prerequisites

1. Phase 1 SQL applied on `gparc` and `gpare` (`PostStatus` columns + `C_LB_Pending_Cash_Receipts_V`).
2. eConnect Integration Service **Running** on the host that runs this exe.
3. `Microsoft.Dynamics.GP.eConnect.dll` available at runtime (default path under `eConnect 18.0\API`, overridable via `CashReceiptPoster:EConnectDllPath`).
4. Connection strings for `FinancialDb` (CAHFS/gparc) and `EquineFinancialDb` (EQUINE/gpare).

The poster **builds without** the eConnect SDK on developer machines (XML is built in code; the eConnect assembly is loaded at runtime on the GP host).

## Phase 3 — Posting loop

```
Read C_LB_Pending_Cash_Receipts_V
  → validate row
  → eConnect taRMCashReceiptInsert (one receipt per check)
  → success: PostStatus = Posted (+ ExternalDocNumber)
  → failure: PostStatus = Failed (+ PostError), continue next row
  → summary + exit code
```

Maps to old IM scripts: After Document = Posted, Document Error = Failed, After Integration = summary/notify.

## Build

```bat
dotnet restore "CAHFS Recharges\LockboxCashReceiptPoster\LockboxCashReceiptPoster.csproj"
dotnet build "CAHFS Recharges\LockboxCashReceiptPoster\LockboxCashReceiptPoster.csproj" -c Release
```

## Run

```bat
REM Preview only (no eConnect, no status updates)
LockboxCashReceiptPoster.exe --company CAHFS --dry-run

REM Cap batch size (Test)
LockboxCashReceiptPoster.exe --company CAHFS --max 5

REM Full post
LockboxCashReceiptPoster.exe --company CAHFS
LockboxCashReceiptPoster.exe --company EQUINE
```

Exit codes: `0` success (or nothing pending / dry-run), `1` one or more posts failed, `2` bad args/config.

Logs: `logs\cash-receipt-{COMPANY}-{yyyyMMdd}.log`

## Config

See full runbook: [`Docs/Lockbox_CashReceiptPoster_Runbook.md`](../Docs/Lockbox_CashReceiptPoster_Runbook.md)

| File | Use |
|---|---|
| `appsettings.json` | Base (companies, checkbook, eConnect path, AWS profile) |
| `appsettings.Development.json` | Local / MaxRows=10 |
| `appsettings.Test.json` | Test host / MaxRows=50 |
| `appsettings.Production.json` | Prod host / MaxRows=0 (all) |

Set `DOTNET_ENVIRONMENT=Development|Test|Production`. **SQL connection strings come from AWS Parameter Store** (`/{Environment}` + `/Shared`, profile `cahfs`) — same as CAEI. Leave `ConnectionStrings` empty in JSON.

AWS settings:

- `AWS:Profile` = `cahfs`
- `AWS:ProfilesLocation` = `C:\cahfs-caei\awscredentials`

## Field mapping (IM)

| Staging / pending view | eConnect `taRMCashReceiptInsert` |
|---|---|
| `BillingId` | `CUSTNMBR` |
| `CheckAmount` | `DOCAMNT` |
| `CheckNumber` | `CHEKNMBR` |
| `FileName` (≤15, no ext) | `BACHNUMB` |
| config | `CHEKBKID` |

## Retry Failed rows

Pending view includes `Failed` (anything not `Posted`). The next run retries them automatically. To clear error text after a fix, see [`Sql/Lockbox/003_Reset_Failed_For_Retry.sql`](../Sql/Lockbox/003_Reset_Failed_For_Retry.sql).
