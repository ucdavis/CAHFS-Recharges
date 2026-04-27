using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CAHFS_Recharges.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CAHFS_Recharges.Services
{
    public sealed class StarLimsCoaWritebackService
    {
        private readonly StarLIMSContext _db;
        private readonly ILogger<StarLimsCoaWritebackService> _log;

        public StarLimsCoaWritebackService(StarLIMSContext db, ILogger<StarLimsCoaWritebackService> log)
        {
            _db = db;
            _log = log;
        }

        public async Task<(bool Success, string Message)> TryWritebackAsync(
            string? docNumber,
            string? newCoa,
            string? oldRawChargeNo,
            CancellationToken ct = default)
        {
            if (!StarLimsDocNumberParser.TryParse(docNumber, out var folderNo, out var invoiceId, out var parseErr))
            {
                _log.LogWarning("StarLIMS write-back skipped: unable to parse DocNumber. DocNumber={DocNumber} Err={Err}", docNumber, parseErr);
                return (false, "StarLIMS update skipped: Document # is not in the expected format.");
            }

            var coa = (newCoa ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(coa))
                return (false, "StarLIMS update skipped: new COA is empty.");

            var oldCharge = string.IsNullOrWhiteSpace(oldRawChargeNo) ? null : oldRawChargeNo.Trim();

            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

                var folderParam = new SqlParameter("@FolderNo", folderNo);
                var invoiceParam = new SqlParameter("@InvoiceId", invoiceId);
                var coaParam = new SqlParameter("@NewChargeNo", coa);

                var gpRows = await _db.Database.ExecuteSqlRawAsync(@"
UPDATE dbo.C_GP_INVOICES
SET CHARGENO = @NewChargeNo
WHERE FOLDERNO = @FolderNo AND INVOICEID = @InvoiceId
", parameters: new[] { folderParam, invoiceParam, coaParam }, cancellationToken: ct);

                if (gpRows != 1)
                {
                    await tx.RollbackAsync(ct);
                    _log.LogWarning("StarLIMS write-back failed: C_GP_INVOICES affected rows != 1. Folder={FolderNo} InvoiceID={InvoiceId} Rows={Rows}", folderNo, invoiceId, gpRows);
                    return (false, "StarLIMS update failed (C_GP_INVOICES row not uniquely found).");
                }

                var invRows = await _db.Database.ExecuteSqlRawAsync(@"
UPDATE dbo.INVOICES
SET CHARGENO = @NewChargeNo
WHERE FOLDERNO = @FolderNo AND INVOICEID = @InvoiceId
", parameters: new[] { folderParam, invoiceParam, coaParam }, cancellationToken: ct);

                if (invRows != 1)
                {
                    await tx.RollbackAsync(ct);
                    _log.LogWarning("StarLIMS write-back failed: INVOICES affected rows != 1. Folder={FolderNo} InvoiceID={InvoiceId} Rows={Rows}", folderNo, invoiceId, invRows);
                    return (false, "StarLIMS update failed (INVOICES row not uniquely found).");
                }

                if (!string.IsNullOrWhiteSpace(oldCharge))
                {
                    var oldChargeParam = new SqlParameter("@OldChargeNo", oldCharge);
                    var roleRows = await _db.Database.ExecuteSqlRawAsync(@"
UPDATE dbo.folder_roles
SET ChargeNo = @NewChargeNo
WHERE FolderNo = @FolderNo AND ChargeNo = @OldChargeNo
", parameters: new[] { folderParam, oldChargeParam, coaParam }, cancellationToken: ct);

                    if (roleRows != 1)
                    {
                        await tx.RollbackAsync(ct);
                        _log.LogWarning("StarLIMS write-back failed: folder_roles affected rows != 1. Folder={FolderNo} OldCharge={OldCharge} Rows={Rows}", folderNo, oldCharge, roleRows);
                        return (false, "StarLIMS update failed (folder_roles row not uniquely found).");
                    }
                }
                else
                {
                    _log.LogWarning("StarLIMS write-back partial: folder_roles skipped because old RawChargeNo is empty. Folder={FolderNo} InvoiceID={InvoiceId}", folderNo, invoiceId);
                }

                await tx.CommitAsync(ct);
                return (true, "StarLIMS updated.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "StarLIMS write-back exception. DocNumber={DocNumber}", docNumber);
                return (false, "StarLIMS update failed due to an unexpected error.");
            }
        }
    }
}
