using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CAHFS_Recharges.Data;
using CAHFS_Recharges.Models;
using CAHFS.GraphQl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CAHFS_Recharges.Services
{
    public sealed class AggieEnterpriseJournalUploadService
    {
        private const string BoundaryAppName = "UCD CAHFS Billing";
        private const string JournalSourceName = "UCD CAHFS Billing";
        private const string JournalCategoryName = "UCD Recharge";

        private readonly FinancialContext _db;
        private readonly IAggieEnterpriseClient _ae;
        private readonly AggieEnterpriseSendGatekeeper _gatekeeper;
        private readonly ILogger<AggieEnterpriseJournalUploadService> _log;
        private readonly IAeHttpTraceStore _trace;


        public AggieEnterpriseJournalUploadService(
            FinancialContext db,
            IAggieEnterpriseClient ae,
            AggieEnterpriseSendGatekeeper gatekeeper,
            ILogger<AggieEnterpriseJournalUploadService> log,
            IAeHttpTraceStore trace)
        {
            _db = db;
            _ae = ae;
            _gatekeeper = gatekeeper;
            _log = log;
            _trace = trace;
        }

        // Preview + Download JSON
        public async Task<PreviewResult> BuildPreviewAsync(Guid batchId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var items = await _db.FeedItems
                .AsNoTracking()
                .Where(i => i.BatchID == batchId)
                .ToListAsync(ct);

            var itemCount = items.Count;
            var lineCount = itemCount * 2;

            var total = items.Sum(i => i.TotalCharge);
            var debitTotal = total;
            var creditTotal = total;

            // using 2-decimal compare to avoid tiny rounding noise
            var isBalanced = Math.Round(debitTotal, 2) == Math.Round(creditTotal, 2);

            return new PreviewResult(itemCount, lineCount, debitTotal, creditTotal, isBalanced);
        }

        public async Task<string> BuildPayloadJsonAsync(Guid batchId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await _db.FeedBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BatchID == batchId, ct);

            if (batch == null)
                return "{ \"error\": \"Batch not found.\" }";

            var items = await _db.FeedItems
                .AsNoTracking()
                .Where(i => i.BatchID == batchId)
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.RecordID)
                .ToListAsync(ct);

            if (items.Count == 0)
                return "{ \"error\": \"No items found for this batch.\" }";

            var requestInput = BuildRequest(batch, items);

            // OLD (caused manual API tests to fail if you copy/paste JSON as variables):
            // - default serialization emits PascalCase ("Header", "Payload")
            // - GraphQL expects camelCase ("header", "payload")
            //
            // var jsonOptions = new JsonSerializerOptions
            // {
            //     WriteIndented = true,
            //     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            // };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Serialize(requestInput, jsonOptions);
        }

        // Send to AE (Gatekeep + Build + glJournalRequest mutation)

        public async Task<SendResult> SendBatchAsync(Guid batchId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // 1) Gatekeeping
            var gate = await _gatekeeper.CanSendBatchAsync(batchId, ct);
            if (!gate.CanSend)
                return new SendResult(false, gate.Message, null);

            // 2) Load batch + items
            var batch = await _db.FeedBatches.FirstOrDefaultAsync(b => b.BatchID == batchId, ct);
            if (batch == null)
                return new SendResult(false, "Batch not found.", null);

            var items = await _db.FeedItems
                .Where(i => i.BatchID == batchId)
                .OrderBy(i => i.TransactionDate)
                .ThenBy(i => i.RecordID)
                .ToListAsync(ct);

            if (items.Count == 0)
                return new SendResult(false, "No items found for this batch.", null);

            // 3) Build request input
            var requestInput = BuildRequest(batch, items);

            // 4) Call mutation
            try
            {
                var op = await _ae.GlJournalRequest.ExecuteAsync(requestInput, ct);

                var gqlErrorsText = JoinGraphQlErrors(op.Errors);

                var data = op.Data?.GlJournalRequest;
                if (data == null)
                {
                    // pull last HTTP trace (403 body etc)
                    var last = _trace.GetLastError();
                    var httpDetail = last == null ? null : last.ToMultilineText();

                    batch.AERequestStatus = "Error";
                    batch.ErrorDetail =
                        !string.IsNullOrWhiteSpace(httpDetail) ? httpDetail :
                        string.IsNullOrWhiteSpace(gqlErrorsText)
                            ? "AE returned no data."
                            : $"AE returned no data. GraphQL: {gqlErrorsText}";

                    await _db.SaveChangesAsync(ct);
                    return new SendResult(false, batch.ErrorDetail ?? "AE returned no data.", null);
                }

                var reqId = data.RequestStatus.RequestId;
                batch.AEConsumerRequestID = reqId;

                var statusText = data.RequestStatus.RequestStatus.ToString();
                batch.AERequestStatus = string.IsNullOrWhiteSpace(statusText) ? "Pending" : statusText;

                var validationErrorText = NormalizeErrorMessages(data.ValidationResults?.ErrorMessages);
                var requestStatusErrText = NormalizeErrorMessages(data.RequestStatus.ErrorMessages);
                var processingErrText = NormalizeErrorMessages(data.ProcessingResult?.ErrorMessages);

                batch.ErrorDetail =
                    !string.IsNullOrWhiteSpace(validationErrorText) ? validationErrorText :
                    !string.IsNullOrWhiteSpace(processingErrText) ? processingErrText :
                    !string.IsNullOrWhiteSpace(requestStatusErrText) ? requestStatusErrText :
                    (!string.IsNullOrWhiteSpace(gqlErrorsText) ? gqlErrorsText : null);

                if (data.ValidationResults != null && data.ValidationResults.Valid == false)
                    batch.AERequestStatus = "Error";

                await _db.SaveChangesAsync(ct);

                var ok = !(data.ValidationResults?.Valid == false);
                var msg = batch.AERequestStatus.Equals("Error", StringComparison.OrdinalIgnoreCase)
                    ? (batch.ErrorDetail ?? "AE validation failed.")
                    : "Submitted to Aggie Enterprise successfully (Pending).";

                return new SendResult(ok, msg, reqId);
            }
            catch (Exception ex)
            {
                var last = _trace.GetLastError();
                var httpDetail = last == null ? null : last.ToMultilineText();

                batch.AERequestStatus = "Error";
                batch.ErrorDetail =
                    !string.IsNullOrWhiteSpace(httpDetail)
                        ? httpDetail
                        : ex.ToString();   // full stack trace if no HTTP trace exists

                await _db.SaveChangesAsync(ct);
                return new SendResult(false, batch.ErrorDetail ?? "AE call failed.", null);
            }
        }

        // Poll Status (glJournalRequestStatus query)

        public async Task<StatusResult> CheckStatusAsync(Guid batchId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await _db.FeedBatches.FirstOrDefaultAsync(b => b.BatchID == batchId, ct);
            if (batch == null)
                return new StatusResult(false, "Batch not found.", null);

            if (batch.AEConsumerRequestID == null)
                return new StatusResult(false, "This batch has no AEConsumerRequestID yet (not submitted).", null);

            var op = await _ae.GlJournalRequestStatus.ExecuteAsync(batch.AEConsumerRequestID.Value, ct);

            var gqlErrorsText = JoinGraphQlErrors(op.Errors);

            var data = op.Data?.GlJournalRequestStatus;
            if (data == null)
                return new StatusResult(false,
                    string.IsNullOrWhiteSpace(gqlErrorsText)
                        ? "AE returned no status data."
                        : $"AE returned no status data. GraphQL: {gqlErrorsText}",
                    batch.AEConsumerRequestID);

            var statusText = data.RequestStatus.RequestStatus.ToString();
            batch.AERequestStatus = string.IsNullOrWhiteSpace(statusText) ? "Unknown" : statusText;

            var validationErr = NormalizeErrorMessages(data.ValidationResults?.ErrorMessages);
            var processingErr = NormalizeErrorMessages(data.ProcessingResult?.ErrorMessages);
            var requestErr = NormalizeErrorMessages(data.RequestStatus.ErrorMessages);

            batch.ErrorDetail =
                !string.IsNullOrWhiteSpace(validationErr) ? validationErr :
                !string.IsNullOrWhiteSpace(processingErr) ? processingErr :
                !string.IsNullOrWhiteSpace(requestErr) ? requestErr :
                (!string.IsNullOrWhiteSpace(gqlErrorsText) ? gqlErrorsText : null);

            await _db.SaveChangesAsync(ct);

            return new StatusResult(true, $"AE Status: {batch.AERequestStatus}", batch.AEConsumerRequestID);
        }

        // Build Request (Header + Payload)

        private GlJournalRequestInput BuildRequest(FeedBatch batch, List<FeedItem> items)
        {
            var acctDt = (batch.AETransactionDate ?? batch.DateSent ?? DateTime.UtcNow).Date;

            // TESTING MODE:
            // Use Pacific Time "today" so AE doesn't see it as a future date
            //var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            //var acctDt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacific).Date;
            var accountingDate = DateOnly.FromDateTime(acctDt);

            var nowStamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            var consumerRef = $"Journal Upload CAHFS {nowStamp}";

            var header = new ActionRequestHeaderInput
            {
                BoundaryApplicationName = BoundaryAppName,
                ConsumerId = BoundaryAppName,
                ConsumerNotes = batch.AEConsumnerNotes,
                ConsumerReferenceId = consumerRef,
                ConsumerTrackingId = consumerRef,
                BatchRequest = null
            };

            var journalName = string.IsNullOrWhiteSpace(batch.AEJournalName)
                ? $"CAHFS Journal {acctDt:yyyy-MM-dd}"
                : batch.AEJournalName;

            // journalReference in AE is max 25 chars; your DB column is 50, so enforce 25.
            var journalReference = string.IsNullOrWhiteSpace(batch.AEJournalReference)
                ? $"CAHFS_{acctDt:yyyyMMdd}"
                : batch.AEJournalReference;

            journalReference = SafeTrim(journalReference, 25) ?? $"CAHFS_{acctDt:yyyyMMdd}";

            // IMPORTANT CHANGE:
            // OLD: we were setting AccountingPeriodName from acctDt (can land in closed period and fail validation).
            // var periodName = acctDt.ToString("MMM-yy", CultureInfo.InvariantCulture);
            // AccountingPeriodName = SafeTrim(periodName, 15) ?? periodName,
            //
            // NEW: omit AccountingPeriodName and let AE pick the current open period based on AccountingDate.
            // (Matches manual payload that validated successfully.)
            string? accountingPeriodName = null;

            var payload = new GlJournalInput
            {
                AccountingDate = accountingDate,
                AccountingPeriodName = accountingPeriodName, // keep null
                JournalSourceName = JournalSourceName,
                JournalCategoryName = JournalCategoryName,
                JournalName = SafeTrim(journalName, 100) ?? journalName,
                JournalReference = journalReference,
                JournalDescription = SafeTrim(batch.AEJournalDescription ?? journalName, 240)
                    ?? (batch.AEJournalDescription ?? journalName),

                JournalLines = BuildLines(batch, items, acctDt).ToList()
            };

            return new GlJournalRequestInput
            {
                Header = header,
                Payload = payload
            };
        }

        // Build Lines (2 per FeedItem: debit + credit)
        //
        // KEY FIXES WE DISCUSSED:
        //  - Don’t always set glSegmentString.
        //  - If the line is PPM, you must set ppmSegmentString (and keep glSegmentString = null).
        //  - If the line is GL, set glSegmentString (and keep ppmSegmentString = null).
        //  - Make sure the string you put into glSegmentString is always GL11,
        //    or you’ll keep getting “Expected GlSegmentString” schema errors.
        //
        // AE rules applied:
        //  - DebitAmount/CreditAmount are generated as strings because of StrawberryShake
        //    generated classes currently have DebitAmount/CreditAmount typed as string (even though
        //    schema says NonNegativeFloat).
        //  - ExternalSystemIdentifier max 10 chars
        //  - ExternalSystemReference max 25 chars
        //  - Glide JournalLineNumber is string in your generated code (even though schema says PositiveInt).
        //  - Glide UdfString1/UdfString2 max 50 chars
        private IEnumerable<GlJournalLineInput> BuildLines(FeedBatch batch, List<FeedItem> items, DateTime fallbackDate)
        {
            var lineNo = 1;

            // Precompute a stable 12-char batch short id for external refs (12 + 1 + 12 = 25)
            var batchShort12 = batch.BatchID.ToString("N").Substring(0, 12);

            foreach (var i in items)
            {
                // Amount
                var amount = Convert.ToDouble(i.TotalCharge, CultureInfo.InvariantCulture);

                // Dates
                var txDate = (i.TransactionDate == default ? fallbackDate : i.TransactionDate).Date;
                var txDateOnly = DateOnly.FromDateTime(txDate);

                // External IDs with strict max lengths
                var recordN = i.RecordID.ToString("N");
                var externalId10 = recordN.Substring(0, 10); // <= 10

                var recordShort12 = recordN.Substring(0, 12);
                var externalRef25 = $"{batchShort12}-{recordShort12}"; // 25 total

                // Glide recommended fields
                var udfQty = i.Quantity.HasValue ? Convert.ToDouble(i.Quantity.Value, CultureInfo.InvariantCulture) : (double?)null;
                var udfUnitPrice = i.UnitPrice.HasValue ? Convert.ToDouble(i.UnitPrice.Value, CultureInfo.InvariantCulture) : (double?)null;
                var udfTotal = Convert.ToDouble(i.TotalCharge, CultureInfo.InvariantCulture);

                var udfTestCode = SafeTrim(i.TestCode, 50);
                var udfTestName = SafeTrim(i.TestName, 50);

                var desc = BuildLineDescription(i); // max 100

                // Determine segment type for DEBIT and CREDIT separately.
                // - If it starts with SP/CP -> PPM
                // - Else -> GL

                var debitIsPpm = IsPpm(i.DebitChartString);
                var creditIsPpm = IsPpm(i.CreditChartString);

                
                // DEBIT line
                yield return new GlJournalLineInput
                {
                    DebitAmount = amount.ToString("F2", CultureInfo.InvariantCulture),
                    CreditAmount = null,

                    // IMPORTANT CHANGE:
                    // OLD: always set GlSegmentString from DebitChartString
                    // GlSegmentString = i.DebitChartString?.Trim(),
                    //
                    // NEW:
                    // - If PPM: set ppmSegmentString and keep glSegmentString = null
                    // - If GL: set glSegmentString (forced to GL11) and keep ppmSegmentString = null
                    GlSegmentString = debitIsPpm ? null : ToGl11(i.DebitChartString),
                    PpmSegmentString = debitIsPpm ? ToPpmString(i.DebitChartString) : null,
                    PpmComment = null,

                    ExternalSystemIdentifier = externalId10,
                    ExternalSystemReference = externalRef25,

                    Glide = new GlideInput
                    {
                        // NOTE: your generated code has JournalLineNumber as string
                        JournalLineNumber = (lineNo++).ToString(CultureInfo.InvariantCulture),
                        LineDescription = desc,
                        TransactionDate = txDateOnly,

                        UdfNumeric1 = udfQty,
                        UdfNumeric2 = udfUnitPrice,
                        UdfNumeric3 = udfTotal,

                        UdfString1 = udfTestCode,
                        UdfString2 = udfTestName
                    }
                };

                // CREDIT line
                yield return new GlJournalLineInput
                {
                    DebitAmount = null,
                    CreditAmount = amount.ToString("F2", CultureInfo.InvariantCulture),

                    // IMPORTANT CHANGE (same rules as DEBIT):
                    GlSegmentString = creditIsPpm ? null : ToGl11(i.CreditChartString),
                    PpmSegmentString = creditIsPpm ? ToPpmString(i.CreditChartString) : null,
                    PpmComment = null,

                    ExternalSystemIdentifier = externalId10,
                    ExternalSystemReference = externalRef25,

                    Glide = new GlideInput
                    {
                        JournalLineNumber = (lineNo++).ToString(CultureInfo.InvariantCulture),
                        LineDescription = desc,
                        TransactionDate = txDateOnly,

                        UdfNumeric1 = udfQty,
                        UdfNumeric2 = udfUnitPrice,
                        UdfNumeric3 = udfTotal,

                        UdfString1 = udfTestCode,
                        UdfString2 = udfTestName
                    }
                };
            }
        }

        // Segment Helpers
        private static bool IsPpm(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            raw = raw.Trim();
            return raw.StartsWith("SP", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("CP", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ToGl11(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Remove any existing dashes/spaces
            var cc = raw.Replace("-", "").Trim();

            // GL11 requires 59 chars before dashing
            cc = (cc + new string('0', 59)).Substring(0, 59);

            // Build strictly as GL11 and force required trailing segments
            return
                cc.Substring(0, 4) + "-" +
                cc.Substring(4, 5) + "-" +
                cc.Substring(9, 7) + "-" +
                cc.Substring(16, 6) + "-" +
                cc.Substring(22, 2) + "-" +
                cc.Substring(24, 3) + "-" +
                cc.Substring(27, 10) + "-" +
                cc.Substring(37, 6) + "-" +
                "0000-000000-000000";
        }

        private static string? ToPpmString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Your upstream SQL already formats PPM as hyphen-delimited strings.
            // We just normalize whitespace here.
            // Examples:
            //  - Required Only: FP00000001-TASK01-0000000-000000
            //  - Sponsored:     K300000001-TASK01-0000000-000000-0000000-00000
            return raw.Trim();
        }

        // Helpers (string lengths, descriptions)

        private static string? SafeTrim(string? s, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length <= maxLen ? s : s.Substring(0, maxLen);
        }

        private static string BuildLineDescription(FeedItem i)
        {
            var baseText = $"{i.TestCode?.Trim()} {i.TestName?.Trim()}".Trim();
            if (string.IsNullOrWhiteSpace(baseText))
                baseText = "CAHFS Billing";

            // AE: TrimmedString100
            return baseText.Length <= 100 ? baseText : baseText.Substring(0, 100);
        }

        // Error Helpers
        private static string? NormalizeErrorMessages(string? err)
        {
            if (string.IsNullOrWhiteSpace(err)) return null;
            return err.Trim();
        }

        private static string? NormalizeErrorMessages(IReadOnlyList<string>? list)
        {
            if (list == null || list.Count == 0) return null;
            return string.Join(" | ", list.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static string? JoinGraphQlErrors(IReadOnlyList<StrawberryShake.IClientError>? errors)
        {
            if (errors == null || errors.Count == 0) return null;
            return string.Join(" | ", errors.Select(e => e.Message).Where(m => !string.IsNullOrWhiteSpace(m)));
        }

        // Result DTOs
        public sealed record PreviewResult(int ItemCount, int JournalLineCount, decimal DebitTotal, decimal CreditTotal, bool IsBalanced);
        public sealed record SendResult(bool Success, string Message, Guid? RequestId);
        public sealed record StatusResult(bool Success, string Message, Guid? RequestId);
    }
}
