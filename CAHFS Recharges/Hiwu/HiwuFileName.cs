using System.Text.RegularExpressions;

namespace CAHFS_Recharges.Hiwu
{
    public readonly record struct HiwuFileNameInfo(bool IsAmendment, string? OriginalNamePrefix);

    public static class HiwuFileName
    {
        private static readonly Regex BodyPattern = new(
            @"^(?<track>.+)-(?<type>[^-]+)-(?<date>\d{8})_",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FileDatePattern = new(
            @"_(\d{8})_(\d{8})_",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static HiwuFileNameInfo Read(string remoteFileName)
        {
            var name = Path.GetFileName(remoteFileName.Trim());
            if (name.StartsWith("COMBINED_", StringComparison.OrdinalIgnoreCase))
                name = name["COMBINED_".Length..];

            if (!name.StartsWith("11009_", StringComparison.OrdinalIgnoreCase))
                return new HiwuFileNameInfo(false, null);

            name = name["11009_".Length..];
            var isAmendment = name.StartsWith("AMENDED_", StringComparison.OrdinalIgnoreCase);
            if (isAmendment)
                name = name["AMENDED_".Length..];

            var match = BodyPattern.Match(name);
            if (!match.Success)
                return new HiwuFileNameInfo(isAmendment, null);

            var prefix = "11009_"
                + match.Groups["track"].Value
                + "-"
                + match.Groups["type"].Value
                + "-"
                + match.Groups["date"].Value
                + "_";
            return new HiwuFileNameInfo(isAmendment, prefix);
        }

        public static bool TryReadFileDate(string remoteFileName, out DateTime fileDate)
        {
            fileDate = default;
            var name = Path.GetFileName(remoteFileName.Trim());
            var match = FileDatePattern.Match(name);
            if (!match.Success)
                return false;

            return TryParseMmddyyyy(match.Groups[2].Value, out fileDate);
        }

        private static bool TryParseMmddyyyy(string value, out DateTime date)
        {
            date = default;
            if (value.Length != 8
                || !int.TryParse(value[..2], out var month)
                || !int.TryParse(value[2..4], out var day)
                || !int.TryParse(value[4..], out var year))
                return false;

            try
            {
                date = new DateTime(year, month, day);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public static HiwuImportFile? ChooseEarliest(IEnumerable<HiwuImportFile> candidates, string namePrefix)
        {
            return candidates
                .Where(file => !file.IsAmendment
                    && file.RemoteFileName.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.ReceivedUtc)
                .ThenBy(file => file.FileId)
                .FirstOrDefault();
        }
    }
}
