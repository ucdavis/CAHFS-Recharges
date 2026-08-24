using LockboxCashReceiptPoster.Abstractions;
using LockboxCashReceiptPoster.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LockboxCashReceiptPoster
{
    /// CLI / run switches for Phase 3 posting loop.
    public sealed class RunArgs
    {
        public string Company { get; set; } = "";
        public bool DryRun { get; set; }
        public int? MaxRows { get; set; }
    }

    /// Phase 3 posting loop (IM script equivalent):
    /// Before Integration → count pending
    /// For each document → post taRMCashReceiptInsert
    /// After Document → MarkPosted (imported)
    /// Document Error → MarkFailed (error), continue
    /// After Integration → notify + exit code
    public sealed class CashReceiptRunService
    {
        private readonly RunArgs _runArgs;
        private readonly IPendingReceiptSource _source;
        private readonly ICashReceiptPoster _poster;
        private readonly IPostStatusStore _statusStore;
        private readonly IFailureNotifier _notifier;
        private readonly ILogger<CashReceiptRunService> _logger;

        public CashReceiptRunService(
            RunArgs runArgs,
            IPendingReceiptSource source,
            ICashReceiptPoster poster,
            IPostStatusStore statusStore,
            IFailureNotifier notifier,
            ILogger<CashReceiptRunService> logger)
        {
            _runArgs = runArgs;
            _source = source;
            _poster = poster;
            _statusStore = statusStore;
            _notifier = notifier;
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken cancellationToken = default)
        {
            var company = _runArgs.Company;
            var pending = await _source.GetPendingAsync(_runArgs.MaxRows, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Company={Company} DryRun={DryRun} MaxRows={MaxRows} pending count={Count}",
                company, _runArgs.DryRun, _runArgs.MaxRows?.ToString() ?? "all", pending.Count);

            if (pending.Count == 0)
            {
                WriteSummary(company, 0, 0, 0, _runArgs.DryRun);
                return 0;
            }

            if (_runArgs.DryRun)
            {
                foreach (var row in pending)
                {
                    _logger.LogInformation(
                        "DRY-RUN StagingId={StagingId} Customer={Customer} Check={Check} Amount={Amount} File={File}",
                        row.StagingId, row.CustomerId, row.CheckNumber, row.Amount, row.FileName);
                }

                WriteSummary(company, wouldPost: pending.Count, posted: 0, failed: 0, dryRun: true);
                return 0;
            }

            var posted = 0;
            var failed = 0;
            var errors = new List<string>();

            foreach (var row in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip blank customer for now (do not mark Failed — leave for later fix).
                if (string.IsNullOrWhiteSpace(row.CustomerId))
                {
                    _logger.LogWarning(
                        "Skipping StagingId={StagingId} — blank BillingId/CustomerId",
                        row.StagingId);
                    continue;
                }

                var validationError = ValidateBeforePost(row);
                if (validationError != null)
                {
                    await _statusStore.MarkFailedAsync(row.StagingId, validationError, cancellationToken)
                        .ConfigureAwait(false);
                    failed++;
                    var detail = $"StagingId={row.StagingId}; Customer={row.CustomerId}; Check={row.CheckNumber}; {validationError}";
                    errors.Add(detail);
                    _logger.LogError("Validation failed {Detail}", detail);
                    continue;
                }

                var result = await _poster.PostAsync(row, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    // IM After Document → EVENT_ACTION = 'imported'
                    await _statusStore.MarkPostedAsync(row.StagingId, result.DocumentNumber, cancellationToken)
                        .ConfigureAwait(false);
                    posted++;
                    _logger.LogInformation(
                        "Posted StagingId={StagingId} Customer={Customer} Amount={Amount} Doc={Doc} BatchFile={File}",
                        row.StagingId, row.CustomerId, row.Amount, result.DocumentNumber, row.FileName);
                }
                else
                {
                    // IM Document Error → EVENT_ACTION = 'error'
                    var message = result.ErrorMessage ?? "Unknown eConnect error";
                    if (!string.IsNullOrEmpty(result.ErrorCode))
                        message = $"[{result.ErrorCode}] {message}";

                    await _statusStore.MarkFailedAsync(row.StagingId, message, cancellationToken)
                        .ConfigureAwait(false);
                    failed++;
                    var detail = $"StagingId={row.StagingId}; Customer={row.CustomerId}; Check={row.CheckNumber}; {message}";
                    errors.Add(detail);
                    _logger.LogError("Failed {Detail}", detail);
                }
            }

            // IM After Integration
            await _notifier.NotifyAsync(company, posted, failed, errors, cancellationToken).ConfigureAwait(false);
            WriteSummary(company, wouldPost: pending.Count, posted, failed, dryRun: false);

            _logger.LogInformation(
                "Company={Company} complete posted={Posted} failed={Failed}",
                company, posted, failed);

            return failed > 0 ? 1 : 0;
        }

        ///Local guards before calling eConnect (cheap fail → Failed status)
        internal static string? ValidateBeforePost(CashReceiptRequest row)
        {
            if (string.IsNullOrWhiteSpace(row.CustomerId))
                return "CustomerId (BillingId) is required.";
            if (row.Amount <= 0m)
                return "Amount (CheckAmount) must be greater than zero.";
            if (string.IsNullOrWhiteSpace(row.FileName))
                return "FileName is required for Batch ID.";
            return null;
        }

        private static void WriteSummary(string company, int wouldPost, int posted, int failed, bool dryRun)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("=== Lockbox Cash Receipt Run Summary ===");
            System.Console.WriteLine($"Company : {company}");
            System.Console.WriteLine($"Mode    : {(dryRun ? "DRY-RUN (no eConnect / no status updates)" : "POST")}");
            if (dryRun)
                System.Console.WriteLine($"Would post : {wouldPost}");
            else
            {
                System.Console.WriteLine($"Attempted : {wouldPost}");
                System.Console.WriteLine($"Posted    : {posted}");
                System.Console.WriteLine($"Failed    : {failed}");
            }
            System.Console.WriteLine("========================================");
        }
    }
}
