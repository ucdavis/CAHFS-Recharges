using CAHFS_Recharges.Models;
using Microsoft.AspNetCore.DataProtection;
using System.Text;
using System.Text.Json.Serialization;

namespace CAHFS_Recharges.Services
{
    // Token service to manage token lifecycle
    public interface ITokenService
    {
        Task<string> GetValidTokenAsync();
        Task<string> RefreshTokenAsync();
        void Clear();
        bool IsTokenValid();
    }

    public class TokenService : ITokenService
    {
        private string _currentToken;
        private DateTime _tokenExpiration;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly HttpClient _httpClient;

        public TokenService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _currentToken = string.Empty;
            _tokenExpiration = DateTime.MinValue;
        }

        public bool IsTokenValid()
        {
            // Add buffer time (e.g., 5 minutes) before actual expiration
            return !string.IsNullOrEmpty(_currentToken)
                && _tokenExpiration > DateTime.UtcNow.AddMinutes(5);
        }

        public async Task<string> GetValidTokenAsync()
        {
            if (IsTokenValid())
            {
                return _currentToken;
            }

            return await RefreshTokenAsync();
        }

        public void Clear()
        {
            _currentToken = string.Empty;
            _tokenExpiration = DateTime.MinValue;
        }

        public async Task<string> RefreshTokenAsync()
        {
            // Prevent multiple simultaneous refresh attempts
            await _refreshLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (IsTokenValid())
                {
                    return _currentToken;
                }

                var tokenUrl = HttpHelper.GetSetting<string>("AggieEnterprise", "TokenUrl");
                
                // Add your auth credentials, refresh token, etc.
                var key = HttpHelper.GetSetting<string>("Credentials", "AggieEnterpriseConsumerKey");
                var secret = HttpHelper.GetSetting<string>("Credentials", "AggieEnterpriseConsumerSecret");
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}")));
               
                var content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                ]);

                var response = await _httpClient.PostAsync(tokenUrl, content);
                response.EnsureSuccessStatusCode();

                var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();

                if (tokenResponse != null) 
                {
                    _currentToken = tokenResponse.AccessToken;
                    _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                }

                return _currentToken;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
            [JsonPropertyName("scope")]
            public string Scope { get; set; } = string.Empty;
            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = string.Empty;
        }
    }
}
