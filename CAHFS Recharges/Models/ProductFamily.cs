namespace CAHFS_Recharges.Models
{
    /// Product family for Integration Hub (AE journals vs BofA Lockbox).
    public enum ProductFamily
    {
        AE,
        Lockbox,
        Hiwu
    }

    public static class ProductFamilyExtensions
    {
        public static bool TryParse(string? value, out ProductFamily result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Equals(nameof(ProductFamily.AE), StringComparison.OrdinalIgnoreCase))
            {
                result = ProductFamily.AE;
                return true;
            }

            if (value.Equals(nameof(ProductFamily.Lockbox), StringComparison.OrdinalIgnoreCase))
            {
                result = ProductFamily.Lockbox;
                return true;
            }

            if (value.Equals(nameof(ProductFamily.Hiwu), StringComparison.OrdinalIgnoreCase))
            {
                result = ProductFamily.Hiwu;
                return true;
            }

            return false;
        }

        public static string ToRouteString(this ProductFamily family) => family switch
        {
            ProductFamily.Lockbox => "Lockbox",
            ProductFamily.Hiwu => "Hiwu",
            _ => "AE"
        };
    }
}
