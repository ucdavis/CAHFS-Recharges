using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CAHFS_Recharges.Services
{
    public class AuthorizationMessageHandler : DelegatingHandler
    {
        private readonly ITokenService _tokenService;
        private readonly IAeHttpTraceStore _trace;
        private readonly ILogger<AuthorizationMessageHandler> _log;

        public AuthorizationMessageHandler(
            ITokenService tokenService,
            IAeHttpTraceStore trace,
            ILogger<AuthorizationMessageHandler> log)
        {
            _tokenService = tokenService;
            _trace = trace;
            _log = log;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // capture request info before sending
            var requestSnapshot = await CloneRequestAsync(request, cancellationToken);

            // 1) Send with current token
            var token = await _tokenService.GetValidTokenAsync();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                // network-level failure (no HTTP response)
                _trace.SetLastError(new AeHttpTrace
                {
                    WhenUtc = DateTime.UtcNow,
                    Method = requestSnapshot.Method,
                    Url = requestSnapshot.Url,
                    RequestHeaders = requestSnapshot.Headers,
                    RequestBody = requestSnapshot.Body,
                    StatusCode = null,
                    ResponseHeaders = null,
                    ResponseBody = null,
                    Exception = ex.ToString()
                });

                _log.LogError(ex, "AE HTTP call failed before getting a response.");
                throw;
            }

            // 2) If unauthorized/forbidden, capture body and retry once with fresh token
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken);

                _trace.SetLastError(new AeHttpTrace
                {
                    WhenUtc = DateTime.UtcNow,
                    Method = requestSnapshot.Method,
                    Url = requestSnapshot.Url,
                    RequestHeaders = requestSnapshot.Headers,
                    RequestBody = requestSnapshot.Body,
                    StatusCode = (int)response.StatusCode,
                    ResponseHeaders = response.Headers.ToString(),
                    ResponseBody = body,
                    Exception = null
                });

                _log.LogWarning("AE returned {StatusCode}. Body: {Body}", (int)response.StatusCode, body);

                // dispose response before retry
                response.Dispose();

                // Clear token and retry with a fresh token using a NEW request instance
                _tokenService.Clear();

                var retryToken = await _tokenService.GetValidTokenAsync();
                var retryRequest = await CloneHttpRequestMessageAsync(requestSnapshot, cancellationToken);
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", retryToken);

                var retryResponse = await base.SendAsync(retryRequest, cancellationToken);

                if (retryResponse.StatusCode == HttpStatusCode.Unauthorized || retryResponse.StatusCode == HttpStatusCode.Forbidden)
                {
                    var retryBody = await SafeReadBodyAsync(retryResponse, cancellationToken);

                    _trace.SetLastError(new AeHttpTrace
                    {
                        WhenUtc = DateTime.UtcNow,
                        Method = requestSnapshot.Method,
                        Url = requestSnapshot.Url,
                        RequestHeaders = requestSnapshot.Headers,
                        RequestBody = requestSnapshot.Body,
                        StatusCode = (int)retryResponse.StatusCode,
                        ResponseHeaders = retryResponse.Headers.ToString(),
                        ResponseBody = retryBody,
                        Exception = null
                    });

                    _log.LogWarning("AE retry returned {StatusCode}. Body: {Body}", (int)retryResponse.StatusCode, retryBody);
                }

                return retryResponse;
            }

            return response;
        }

        private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                return response.Content == null ? null : await response.Content.ReadAsStringAsync(ct);
            }
            catch
            {
                return null;
            }
        }

        // request snapshot helpers (so we can retry safely)

        private sealed class RequestSnapshot
        {
            public string Method { get; set; } = "";
            public string Url { get; set; } = "";
            public string Headers { get; set; } = "";
            public string? Body { get; set; }
            public string? ContentType { get; set; }
        }

        private static async Task<RequestSnapshot> CloneRequestAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var snap = new RequestSnapshot
            {
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString() ?? "",
                Headers = request.Headers.ToString()
            };

            if (request.Content != null)
            {
                snap.ContentType = request.Content.Headers.ContentType?.ToString();
                snap.Body = await request.Content.ReadAsStringAsync(ct);
            }

            return snap;
        }

        private static Task<HttpRequestMessage> CloneHttpRequestMessageAsync(RequestSnapshot snap, CancellationToken ct)
        {
            var msg = new HttpRequestMessage(new HttpMethod(snap.Method), snap.Url);

            // NOTE: headers like Authorization are set later
            // Most of the important debug info is captured in trace store already.

            if (!string.IsNullOrEmpty(snap.Body))
            {
                msg.Content = new StringContent(snap.Body, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(snap.ContentType))
                    msg.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(snap.ContentType);
            }

            return Task.FromResult(msg);
        }
    }
}
