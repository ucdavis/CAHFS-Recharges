namespace CAHFS_Recharges.Services.Lockbox
{
    internal static class LockboxBusinessDays
    {
        public static bool IsBusinessDay(DateOnly date) =>
            date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

        public static DateOnly? PreviousBusinessDay(DateOnly date)
        {
            var cursor = date.AddDays(-1);
            for (var i = 0; i < 14; i++)
            {
                if (IsBusinessDay(cursor))
                    return cursor;
                cursor = cursor.AddDays(-1);
            }

            return null;
        }

        public static int CountConsecutiveMissingBusinessDays(
            Func<DateOnly, bool> fileReceivedOnDate,
            DateOnly startDate)
        {
            var count = 0;
            var cursor = startDate;

            for (var i = 0; i < 60; i++)
            {
                if (!IsBusinessDay(cursor))
                {
                    cursor = cursor.AddDays(-1);
                    continue;
                }

                if (fileReceivedOnDate(cursor))
                    break;

                count++;
                cursor = cursor.AddDays(-1);
            }

            return count;
        }
    }
}
