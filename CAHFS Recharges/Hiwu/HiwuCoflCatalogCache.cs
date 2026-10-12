using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Hiwu
{
    /// One TraceFirst catalog for the process. Later files reuse it until it expires.
    public sealed class HiwuCoflCatalogCache
    {
        private static readonly TimeSpan ValidFor = TimeSpan.FromHours(12);
        private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(10);

        private readonly HiwuCoflClient _client;
        private readonly IOptionsMonitor<HiwuOptions> _options;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private HiwuCoflCatalog? _catalog;
        private DateTime _expiresUtc = DateTime.MinValue;

        public HiwuCoflCatalogCache(HiwuCoflClient client, IOptionsMonitor<HiwuOptions> options)
        {
            _client = client;
            _options = options;
        }

        public async Task<HiwuCoflCatalog> GetAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            if (_catalog != null && now < _expiresUtc)
                return _catalog;

            await _gate.WaitAsync(cancellationToken);
            try
            {
                now = DateTime.UtcNow;
                if (_catalog != null && now < _expiresUtc)
                    return _catalog;

                var loaded = await HiwuCoflCatalog.LoadAsync(_client, _options.CurrentValue.Cofl.TestScheduleIds, cancellationToken);
                _catalog = loaded;
                _expiresUtc = now.Add(loaded.LookupErrors.Count == 0 ? ValidFor : RetryAfterError);
                return loaded;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
