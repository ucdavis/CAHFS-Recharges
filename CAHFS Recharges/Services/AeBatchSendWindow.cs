using System;
using CAHFS_Recharges.Models;
using Microsoft.Extensions.Configuration;

namespace CAHFS_Recharges.Services
{
    /// <summary>
    /// Month-slice send window: batch may be Ready mid-week, but send is blocked until after WeekEndDate.
    /// </summary>
    public static class AeBatchSendWindow
    {
        public const string DefaultTimeZoneId = "America/Los_Angeles";

        /// <summary>Business "today" in Hangfire/ops timezone (Pacific).</summary>
        public static DateTime GetBusinessToday(IConfiguration? configuration = null)
        {
            var tzId = configuration?.GetValue<string>("Hangfire:TimeZone");
            if (string.IsNullOrWhiteSpace(tzId))
                tzId = DefaultTimeZoneId;

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                return TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                    return TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz).Date;
                }
                catch
                {
                    return DateTime.Today;
                }
            }
        }

        /// <summary>Inclusive week-slice end (prefer WeekEndDate, else AETransactionDate).</summary>
        public static DateTime? ResolveWeekEndDate(DateTime? weekEndDate, DateTime? aeTransactionDate)
        {
            if (weekEndDate.HasValue)
                return weekEndDate.Value.Date;
            if (aeTransactionDate.HasValue)
                return aeTransactionDate.Value.Date;
            return null;
        }

        public static DateTime? ResolveWeekEndDate(FeedBatch batch) =>
            ResolveWeekEndDate(batch.WeekEndDate, batch.AETransactionDate);

        /// <summary>True while calendar day is on or before week end — more GP postings may still load.</summary>
        public static bool IsWeekInProgress(DateTime? weekEndDate, DateTime businessToday) =>
            weekEndDate.HasValue && businessToday.Date <= weekEndDate.Value.Date;

        public static bool IsWeekInProgress(FeedBatch batch, DateTime businessToday) =>
            IsWeekInProgress(ResolveWeekEndDate(batch), businessToday);

        public static string WeekInProgressLabel(DateTime weekEndDate) =>
            $"Week in progress — send after {weekEndDate:yyyy-MM-dd}";

        public static string WeekInProgressBlockedMessage(DateTime weekEndDate) =>
            $"Blocked: Week slice ends {weekEndDate:yyyy-MM-dd}. Wait until the next day so the daily load can finish this slice, then send.";
    }
}
