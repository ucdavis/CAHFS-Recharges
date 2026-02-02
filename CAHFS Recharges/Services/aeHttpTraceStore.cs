namespace CAHFS_Recharges.Services
{
    public sealed class AeHttpTraceStore : IAeHttpTraceStore
    {
        private AeHttpTrace? _last;

        public void SetLastError(AeHttpTrace trace) => _last = trace;
        public AeHttpTrace? GetLastError() => _last;
    }
}
