using System.Net;
using System.Net.Http.Headers;

namespace CAHFS_Recharges.Services
{
    public class AuthorizationMessageHandler : DelegatingHandler
    {
        private readonly ITokenService _tokenService;

        public AuthorizationMessageHandler(ITokenService tokenService)
        {
            _tokenService = tokenService;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Get a valid token (will refresh if needed)
            var token = await _tokenService.GetValidTokenAsync();

            // Add the token to the authorization header
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await base.SendAsync(request, cancellationToken);
            
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                // if we get unauthorized, clear the token cache and try again
                _tokenService.Clear();
                token = await _tokenService.GetValidTokenAsync();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                response = await base.SendAsync(request, cancellationToken);
            }

            return response;
        }
    }
}
