namespace CAHFS_Recharges.Services.Lockbox
{
    internal static class LockboxFileNameParser
    {
        /// <summary>
        /// Parses business date from filenames like DATA_74483320250527*.
        /// </summary>
        public static bool TryParseBusinessDate(string fileName, string fileNamePrefix, out DateOnly businessDate)
        {
            businessDate = default;
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(fileNamePrefix))
                return false;

            if (!fileName.StartsWith(fileNamePrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var datePart = fileName.AsSpan(fileNamePrefix.Length);
            if (datePart.Length < 8 || !datePart[..8].ToString().All(char.IsDigit))
                return false;

            if (!DateOnly.TryParseExact(datePart[..8].ToString(), "yyyyMMdd", out businessDate))
                return false;

            return true;
        }

        public static bool MatchesTargetDate(string fileName, string fileNamePrefix, DateOnly targetDate) =>
            TryParseBusinessDate(fileName, fileNamePrefix, out var parsed) && parsed == targetDate;
    }
}
