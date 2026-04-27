using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CAHFS.GraphQl;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CAHFS_Recharges.Services
{
    public sealed class StagingCoaValidationService
    {
        private readonly FinancialContext _db;
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly IAggieEnterpriseClient _ae;
        private readonly ILogger<StagingCoaValidationService> _log;

        private static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
        {
            "3110-12107-VHFS001-127302-00-000-0000000000-000000-0000-000000-000000",
            "3110-12107-VHFS001-410000-00-000-0000000000-000000-0000-000000-000000"
        };

        public StagingCoaValidationService(
            FinancialContext db,
            IIntegrationDbResolver dbResolver,
            IAggieEnterpriseClient aggieEnterpriseClient,
            ILogger<StagingCoaValidationService> log)
        {
            _db = db;
            _dbResolver = dbResolver;
            _ae = aggieEnterpriseClient;
            _log = log;
        }

        public async Task<int> ValidatePendingItemsAsync(
            IntegrationType integration,
            int batchSize = 250,
            int maxParallelCalls = 5,
            CancellationToken ct = default)
        {
            var feedItems = _dbResolver.GetFeedItems(integration);
            return await ValidatePendingItemsCoreAsync(
                batchSize,
                maxParallelCalls,
                ct,
                () => feedItems
                    .Where(i => !i.DoNotInclude && (i.DebitStringValid == null || i.CreditStringValid == null))
                    .OrderBy(i => i.BatchID)
                    .ThenBy(i => i.RecordID)
                    .Take(batchSize)
                    .ToListAsync(ct),
                () => _dbResolver.SaveChangesAsync(integration, ct),
                batchIds => UpdateBatchStatusAsync(integration, batchIds, ct));
        }

        public async Task<int> ValidatePendingItemsAsync(
            int batchSize = 250,
            int maxParallelCalls = 5,
            CancellationToken ct = default)
        {
            int updated = 0;

            var cache = new ConcurrentDictionary<string, (bool IsValid, string? Error)>(StringComparer.OrdinalIgnoreCase);

            var affectedBatches = new HashSet<Guid>();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var items = await _db.FeedItems
                    .Where(i => !i.DoNotInclude && (i.DebitStringValid == null || i.CreditStringValid == null))
                    .OrderBy(i => i.BatchID)
                    .ThenBy(i => i.RecordID)
                    .Take(batchSize)
                    .ToListAsync(ct);

                if (!items.Any())
                    break;

                foreach (var i in items)
                    affectedBatches.Add(i.BatchID);

                var work = new List<(Guid RecordId, string Side, string? Coa)>();

                foreach (var item in items)
                {
                    if (item.DebitStringValid == null)
                        work.Add((item.RecordID, "D", item.DebitChartString));

                    if (item.CreditStringValid == null)
                        work.Add((item.RecordID, "C", item.CreditChartString));
                }

                using var throttler = new SemaphoreSlim(maxParallelCalls);

                var results = new ConcurrentDictionary<(Guid RecordId, string Side), (bool IsValid, string? Error)>();

                var tasks = work.Select(async w =>
                {
                    await throttler.WaitAsync(ct);
                    try
                    {
                        var r = await ValidateCoaAsync(w.Coa, cache, ct);
                        results[(w.RecordId, w.Side)] = r;
                    }
                    catch (Exception ex)
                    {
                        results[(w.RecordId, w.Side)] = (false, $"AE API error: {ex.Message}");
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });

                await Task.WhenAll(tasks);

                foreach (var item in items)
                {
                    if (item.DebitStringValid == null && results.TryGetValue((item.RecordID, "D"), out var dr))
                    {
                        item.DebitStringValid = dr.IsValid ? "Valid" : "Invalid";
                        item.DebitValidationError = dr.IsValid ? null : (dr.Error ?? "Invalid");

                        if (!dr.IsValid)
                        {
                            _log.LogWarning("Invalid COA | RecordID={RecordID} | Side=Debit | COA={COA} | Err={Err}",
                                item.RecordID, item.DebitChartString, item.DebitValidationError ?? "-");
                        }
                    }

                    if (item.CreditStringValid == null && results.TryGetValue((item.RecordID, "C"), out var cr))
                    {
                        item.CreditStringValid = cr.IsValid ? "Valid" : "Invalid";
                        item.CreditValidationError = cr.IsValid ? null : (cr.Error ?? "Invalid");

                        if (!cr.IsValid)
                        {
                            _log.LogWarning("Invalid COA | RecordID={RecordID} | Side=Credit | COA={COA} | Err={Err}",
                                item.RecordID, item.CreditChartString, item.CreditValidationError ?? "-");
                        }
                    }
                }

                updated += await _db.SaveChangesAsync(ct);
            }

            await UpdateBatchStatusAsync(affectedBatches, ct);
            return updated;
        }

        private async Task<int> ValidatePendingItemsCoreAsync(
            int batchSize,
            int maxParallelCalls,
            CancellationToken ct,
            Func<Task<List<FeedItem>>> getPendingItems,
            Func<Task<int>> saveChanges,
            Func<HashSet<Guid>, Task> updateBatchStatus)
        {
            int updated = 0;
            var cache = new ConcurrentDictionary<string, (bool IsValid, string? Error)>(StringComparer.OrdinalIgnoreCase);
            var affectedBatches = new HashSet<Guid>();

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var items = await getPendingItems().ConfigureAwait(false);
                if (items.Count == 0)
                    break;

                foreach (var i in items)
                    affectedBatches.Add(i.BatchID);

                var work = new List<(Guid RecordId, string Side, string? Coa)>();
                foreach (var item in items)
                {
                    if (item.DebitStringValid == null)
                        work.Add((item.RecordID, "D", item.DebitChartString));
                    if (item.CreditStringValid == null)
                        work.Add((item.RecordID, "C", item.CreditChartString));
                }

                using var throttler = new SemaphoreSlim(maxParallelCalls);
                var results = new ConcurrentDictionary<(Guid RecordId, string Side), (bool IsValid, string? Error)>();

                var tasks = work.Select(async w =>
                {
                    await throttler.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var r = await ValidateCoaAsync(w.Coa, cache, ct).ConfigureAwait(false);
                        results[(w.RecordId, w.Side)] = r;
                    }
                    catch (Exception ex)
                    {
                        results[(w.RecordId, w.Side)] = (false, $"AE API error: {ex.Message}");
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);

                foreach (var item in items)
                {
                    if (item.DebitStringValid == null && results.TryGetValue((item.RecordID, "D"), out var dr))
                    {
                        item.DebitStringValid = dr.IsValid ? "Valid" : "Invalid";
                        item.DebitValidationError = dr.IsValid ? null : (dr.Error ?? "Invalid");
                        if (!dr.IsValid)
                            _log.LogWarning("Invalid COA | RecordID={RecordID} | Side=Debit | COA={COA} | Err={Err}",
                                item.RecordID, item.DebitChartString, item.DebitValidationError ?? "-");
                    }
                    if (item.CreditStringValid == null && results.TryGetValue((item.RecordID, "C"), out var cr))
                    {
                        item.CreditStringValid = cr.IsValid ? "Valid" : "Invalid";
                        item.CreditValidationError = cr.IsValid ? null : (cr.Error ?? "Invalid");
                        if (!cr.IsValid)
                            _log.LogWarning("Invalid COA | RecordID={RecordID} | Side=Credit | COA={COA} | Err={Err}",
                                item.RecordID, item.CreditChartString, item.CreditValidationError ?? "-");
                    }
                }

                updated += await saveChanges().ConfigureAwait(false);
            }

            await updateBatchStatus(affectedBatches).ConfigureAwait(false);
            return updated;
        }

        private async Task<(bool IsValid, string? Error)> ValidateCoaAsync(
            string? coa,
            ConcurrentDictionary<string, (bool IsValid, string? Error)> cache,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(coa))
                return (false, "COA is null/empty");

            coa = coa.Trim();

            if (Whitelist.Contains(coa))
                return (true, null);

            if (cache.TryGetValue(coa, out var cached))
                return cached;

            try
            {
                // Use ERP validate (supports GL + PPM formats)
                var resp = await _ae.ErpValidateChartstring.ExecuteAsync(
                    segmentString: coa,
                    validateCVRs: true,
                    accountingDate: null,
                    cancellationToken: ct
                );

                if (resp.Errors?.Any() == true)
                {
                    var msg = string.Join(" | ", resp.Errors.Select(e => e.Message));
                    cache[coa] = (false, msg);
                    return (false, msg);
                }

                var vr = resp.Data?.ErpValidateChartstring?.ValidationResponse;
                var valid = vr?.Valid ?? false;

                var err = (vr?.ErrorMessages is { Count: > 0 })
                    ? string.Join(" | ", vr.ErrorMessages)
                    : null;

                cache[coa] = (valid, err);
                return (valid, err);
            }
            catch (Exception ex)
            {
                var msg = $"AE API error: {ex.Message}";
                cache[coa] = (false, msg);
                return (false, msg);
            }
        }

        private async Task UpdateBatchStatusAsync(IntegrationType integration, HashSet<Guid> batchIds, CancellationToken ct)
        {
            if (batchIds.Count == 0)
                return;

            var feedItems = _dbResolver.GetFeedItems(integration);
            foreach (var batchId in batchIds)
            {
                var hasCoaIssue = await feedItems.AnyAsync(i =>
                    i.BatchID == batchId &&
                    !i.DoNotInclude &&
                    i.DebitStringValid != null &&
                    i.CreditStringValid != null &&
                    (i.DebitStringValid != "Valid" || i.CreditStringValid != "Valid"), ct);

                if (hasCoaIssue)
                {
                    await _dbResolver.ExecuteSqlAsync(integration,
                        $"UPDATE C_AE_Feed_Batch SET AERequestStatus = {"Needs Review"} WHERE batchID = {batchId}", ct);
                    continue;
                }

                var hasPending = await feedItems.AnyAsync(i =>
                    i.BatchID == batchId &&
                    !i.DoNotInclude &&
                    (i.DebitStringValid == null || i.CreditStringValid == null), ct);

                if (!hasPending)
                {
                    await _dbResolver.ExecuteSqlAsync(integration,
                        $"UPDATE C_AE_Feed_Batch SET AERequestStatus = {"Ready"} WHERE batchID = {batchId}", ct);
                }
            }
        }

        private async Task UpdateBatchStatusAsync(HashSet<Guid> batchIds, CancellationToken ct)
        {
            if (!batchIds.Any())
                return;

            foreach (var batchId in batchIds)
            {
                var hasCoaIssue = await _db.FeedItems.AnyAsync(i =>
                    i.BatchID == batchId &&
                    !i.DoNotInclude &&
                    i.DebitStringValid != null &&
                    i.CreditStringValid != null &&
                    (i.DebitStringValid != "Valid" || i.CreditStringValid != "Valid"), ct);

                if (hasCoaIssue)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE C_AE_Feed_Batch SET AERequestStatus = {"Needs Review"} WHERE batchID = {batchId}", ct);
                    continue;
                }

                var hasPending = await _db.FeedItems.AnyAsync(i =>
                    i.BatchID == batchId &&
                    !i.DoNotInclude &&
                    (i.DebitStringValid == null || i.CreditStringValid == null), ct);

                if (!hasPending)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE C_AE_Feed_Batch SET AERequestStatus = {"Ready"} WHERE batchID = {batchId}", ct);
                }
            }
        }
    }
}
