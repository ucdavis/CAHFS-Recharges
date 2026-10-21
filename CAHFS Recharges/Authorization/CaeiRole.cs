namespace CAHFS_Recharges.Authorization
{
    public enum CaeiRole
    {
        Viewer = 0,
        Operator = 1,
        Admin = 2
    }

    public static class CaeiCapabilities
    {
        public const string Read = "Read";
        public const string Filter = "Filter";
        public const string Export = "Export";
        public const string Validate = "Validate";
        public const string PostBatch = "PostBatch";
        public const string Mutate = "Mutate";
        public const string AdminTools = "AdminTools";
    }
}
