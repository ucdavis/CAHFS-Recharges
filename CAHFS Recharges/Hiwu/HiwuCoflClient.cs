using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuCoflSession
    {
        public required string Email { get; init; }

        public required string Token { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public long UserId { get; init; }
    }

    /// Logs in to TraceFirst and reuses auth_token until shortly before it expires.
    
    public sealed class HiwuCoflClient
    {
        public const string HttpClientName = "HiwuCofl";

        private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<HiwuOptions> _options;
        private readonly ILogger<HiwuCoflClient> _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private HiwuCoflSession? _session;

        public HiwuCoflClient(
            IHttpClientFactory httpClientFactory,
            IOptions<HiwuOptions> options,
            ILogger<HiwuCoflClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options;
            _logger = logger;
        }

        public async Task<HiwuCoflSession> GetSessionAsync(CancellationToken cancellationToken = default)
        {
            if (IsCurrent(_session))
                return _session!;

            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (IsCurrent(_session))
                    return _session!;

                _session = await LoginAsync(cancellationToken);
                return _session;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task<HttpRequestMessage> CreateRequestAsync(
            HttpMethod method,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            var session = await GetSessionAsync(cancellationToken);
            var request = new HttpRequestMessage(method, BuildApiUri(_options.Value.Cofl.Website, relativePath));
            request.Headers.TryAddWithoutValidation("X-Api-Email", session.Email);
            request.Headers.TryAddWithoutValidation("X-Api-Token", session.Token);
            return request;
        }

        private async Task<HiwuCoflSession> LoginAsync(CancellationToken cancellationToken)
        {
            var cofl = _options.Value.Cofl;
            var missing = HiwuCoflSettingsReader.GetMissingFields(cofl);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "HIWU COFL settings are incomplete (" + string.Join(", ", missing) +
                    "). Expected Parameter Store " + HiwuCoflCredentialPaths.ConfigurationPath + ".");
            }

            var loginUri = BuildLoginUri(cofl.Website, cofl.Username, cofl.Password);
            using var request = new HttpRequestMessage(HttpMethod.Get, loginUri);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HIWU COFL login failed with status {StatusCode}.", (int)response.StatusCode);
                throw new InvalidOperationException(
                    "HIWU COFL login failed with status " + (int)response.StatusCode + ".");
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
            if (body == null || string.IsNullOrWhiteSpace(body.AuthToken))
                throw new InvalidOperationException("HIWU COFL login response did not include auth_token.");

            if (body.Suspended)
                throw new InvalidOperationException("HIWU COFL login was rejected because the account is suspended.");

            if (body.AuthTokenExpiresAt == null)
                throw new InvalidOperationException("HIWU COFL login response did not include auth_token_expires_at.");

            var session = new HiwuCoflSession
            {
                Email = string.IsNullOrWhiteSpace(body.Email) ? cofl.Username : body.Email,
                Token = body.AuthToken,
                ExpiresAt = body.AuthTokenExpiresAt.Value,
                UserId = body.Id
            };

            _logger.LogInformation("HIWU COFL login succeeded. Token expires at {ExpiresAt}.", session.ExpiresAt);
            return session;
        }

        private static bool IsCurrent(HiwuCoflSession? session) =>
            session != null
            && !string.IsNullOrWhiteSpace(session.Token)
            && session.ExpiresAt > DateTimeOffset.UtcNow.Add(RefreshBuffer);

        private static Uri BuildLoginUri(string website, string email, string password)
        {
            var builder = new UriBuilder(RequireAbsoluteWebsite(website));
            var path = builder.Path.TrimEnd('/');
            builder.Path = path + "/api/v1/access_control/login";
            builder.Query = "email=" + Uri.EscapeDataString(email) + "&password=" + Uri.EscapeDataString(password);
            return builder.Uri;
        }

        private static Uri BuildApiUri(string website, string relativePath)
        {
            var builder = new UriBuilder(RequireAbsoluteWebsite(website));
            var path = builder.Path.TrimEnd('/');
            var relative = relativePath.Trim().TrimStart('/');
            builder.Path = string.IsNullOrEmpty(relative) ? path : path + "/" + relative;
            builder.Query = "";
            return builder.Uri;
        }

        private static Uri RequireAbsoluteWebsite(string website)
        {
            if (!Uri.TryCreate(website.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    "HIWU COFL website is not an absolute http or https URL. Expected Parameter Store "
                    + HiwuCoflCredentialPaths.ConfigurationPath + ".");
            }

            return uri;
        }

        private sealed class LoginResponse
        {
            [JsonPropertyName("email")]
            public string? Email { get; set; }

            [JsonPropertyName("auth_token")]
            public string? AuthToken { get; set; }

            [JsonPropertyName("auth_token_expires_at")]
            public DateTimeOffset? AuthTokenExpiresAt { get; set; }

            [JsonPropertyName("suspended")]
            public bool Suspended { get; set; }

            [JsonPropertyName("id")]
            public long Id { get; set; }
        }
    }
}
