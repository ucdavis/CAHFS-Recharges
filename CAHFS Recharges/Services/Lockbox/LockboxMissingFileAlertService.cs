using CAHFS_Recharges.Models;
using CAHFS_Recharges.Models.Lockbox;
using CAHFS_Recharges.Models.Options;
using CAHFS_Recharges.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Services.Lockbox
{
    public sealed class LockboxMissingFileAlertService
    {
        private readonly LockboxOptions _options;
        private readonly IIntegrationDbResolver _dbResolver;
        private readonly ILogger<LockboxMissingFileAlertService> _logger;

        public LockboxMissingFileAlertService(
            IOptions<LockboxOptions> options,
            IIntegrationDbResolver dbResolver,
            ILogger<LockboxMissingFileAlertService> logger)
        {
            _options = options.Value;
            _dbResolver = dbResolver;
            _logger = logger;
        }

        public async Task CheckAndAlertAsync(
            IntegrationType integration,
            DateOnly referenceDate,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Alert.Enabled)
                return;

            var lab = _options.GetLab(integration);
            var lockboxId = lab.LockboxId;
            var threshold = _options.MissingFileAlertAfterBusinessDays;

            var receipts = _dbResolver.GetLockboxDailyReceipts(integration);
            var receiptLookup = await receipts
                .Where(r => r.LockboxId == lockboxId)
                .ToDictionaryAsync(r => r.BusinessDate, r => r.FileReceived, cancellationToken);

            bool FileReceivedOn(DateOnly date) =>
                receiptLookup.TryGetValue(date, out var received) && received;

            var consecutiveMissing = LockboxBusinessDays.CountConsecutiveMissingBusinessDays(
                FileReceivedOn,
                referenceDate);

            var alertStates = _dbResolver.GetLockboxAlertStates(integration);
            var state = await alertStates.FindAsync(new object[] { lockboxId }, cancellationToken);
            if (state == null)
            {
                state = new LockboxAlertState
                {
                    LockboxId = lockboxId,
                    LastConsecutiveMissingDays = consecutiveMissing
                };
                alertStates.Add(state);
            }
            else
            {
                state.LastConsecutiveMissingDays = consecutiveMissing;
            }

            await _dbResolver.SaveChangesAsync(integration, cancellationToken);

            if (consecutiveMissing <= threshold)
                return;

            var minHours = _options.Alert.MinHoursBetweenAlerts > 0
                ? _options.Alert.MinHoursBetweenAlerts
                : 24;

            if (state.LastAlertSentUtc.HasValue &&
                DateTime.UtcNow - state.LastAlertSentUtc.Value < TimeSpan.FromHours(minHours))
            {
                return;
            }

            var integrationLabel = integration == IntegrationType.EQUINE ? "EQUINE" : "CAHFS";
            var message =
                $"Lockbox {lockboxId} ({integrationLabel}): no file received for {consecutiveMissing} consecutive business day(s) " +
                $"(threshold > {threshold} business days). Reference date {referenceDate:yyyy-MM-dd}.";

            _logger.LogError(
                "LOCKBOX_MISSING_FILE_ALERT {LockboxId} {Integration} consecutiveMissing={Days} emailTo={EmailTo} {Message}",
                lockboxId,
                integrationLabel,
                consecutiveMissing,
                string.IsNullOrWhiteSpace(_options.Alert.EmailTo) ? "(not configured)" : _options.Alert.EmailTo,
                message);

            state.LastAlertSentUtc = DateTime.UtcNow;
            await _dbResolver.SaveChangesAsync(integration, cancellationToken);
        }
    }
}
