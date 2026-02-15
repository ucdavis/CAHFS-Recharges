namespace CAHFS_Recharges.Models
{

    /// Supported integration types for CAEI.
    public enum IntegrationType
    {
        CAHFS,
        EQUINE
    }

    public static class IntegrationTypeExtensions
    {
        /// Case-insensitive parse of integration string to IntegrationType.
        public static bool TryParse(string? value, out IntegrationType result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Equals(nameof(IntegrationType.CAHFS), StringComparison.OrdinalIgnoreCase))
            {
                result = IntegrationType.CAHFS;
                return true;
            }

            if (value.Equals(nameof(IntegrationType.EQUINE), StringComparison.OrdinalIgnoreCase))
            {
                result = IntegrationType.EQUINE;
                return true;
            }

            return false;
        }
    }
}
