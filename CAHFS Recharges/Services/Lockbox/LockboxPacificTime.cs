namespace CAHFS_Recharges.Services.Lockbox
{
    internal static class LockboxPacificTime
    {
        public static DateOnly GetPreviousCalendarDate(string? timeZoneId)
        {
            var tz = ResolveTimeZone(timeZoneId);
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            return DateOnly.FromDateTime(localToday.AddDays(-1));
        }

        public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
    }
}
