namespace CAHFS_Recharges.Services
{
    public interface IAeHttpTraceStore
    {
        void SetLastError(AeHttpTrace trace);
        AeHttpTrace? GetLastError();
    }
}
