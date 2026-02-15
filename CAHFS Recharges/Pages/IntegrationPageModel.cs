using CAHFS_Recharges.Authorization;
using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Pages
{
    /// Base class for all integration-aware pages.
    /// Handles integration validation, context, and role checks.
    /// Supports route-based integration via {integration} route parameter.
    public abstract class IntegrationPageModel : PageModel
    {
        private readonly IIntegrationContextService _integrationService;
        private readonly IAuthorizationService _authorizationService;

        /// Route parameter for integration (e.g., "CAHFS" or "EQUINE").
        /// Bound automatically from route template: @page "/Integrations/{integration}/..."
        [BindProperty(SupportsGet = true)]
        public string? Integration { get; set; }

        protected IntegrationType? CurrentIntegration { get; private set; }

        protected IntegrationType? ForcedIntegration { get; set; }

      
        protected bool SkipIntegrationValidation { get; set; }

        protected IntegrationPageModel(
            IIntegrationContextService integrationService,
            IAuthorizationService authorizationService)
        {
            _integrationService = integrationService;
            _authorizationService = authorizationService;
        }

        public override async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context,
            PageHandlerExecutionDelegate next)
        {
       

            if (ForcedIntegration.HasValue)
            {
                CurrentIntegration = ForcedIntegration.Value;
            }
            else if (!string.IsNullOrWhiteSpace(Integration))
            {
                if (IntegrationTypeExtensions.TryParse(Integration, out var fromRoute))
                {
                    CurrentIntegration = fromRoute;
                    _integrationService.SetIntegrationCookie(HttpContext, fromRoute);
                }
                else
                {
                    // Invalid integration value in route - redirect to Home
                    if (!SkipIntegrationValidation)
                    {
                        context.Result = RedirectToPage("/Home");
                        return;
                    }
                }
            }
            else
            {
                CurrentIntegration = _integrationService.Resolve(HttpContext);
            }

            if (!SkipIntegrationValidation && CurrentIntegration == null)
            {
                context.Result = RedirectToPage("/Home");
                return;
            }

            // Set ViewData for layout/partials
            ViewData["Integration"] = CurrentIntegration?.ToString();
            ViewData["IsAdmin"] = await IsAdminAsync();
            ViewData["IsOperator"] = await IsOperatorAsync();

            await next();
        }

        /// Checks if current user has Admin role.
        protected async Task<bool> IsAdminAsync()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return false;

            var result = await _authorizationService.AuthorizeAsync(User, CaeiPolicies.AdminPolicy);
            return result.Succeeded;
        }

        /// Checks if current user has Operator role (or higher).
        protected async Task<bool> IsOperatorAsync()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return false;

            var result = await _authorizationService.AuthorizeAsync(User, CaeiPolicies.OperatorPolicy);
            return result.Succeeded;
        }

        /// Synchronous helper for IsAdmin check (uses cached ViewData if available).
        protected bool IsAdmin()
        {
            if (ViewData["IsAdmin"] is bool cached)
                return cached;

            return IsAdminAsync().GetAwaiter().GetResult();
        }

        /// Synchronous helper for IsOperator check (uses cached ViewData if available).
        protected bool IsOperator()
        {
            if (ViewData["IsOperator"] is bool cached)
                return cached;

            return IsOperatorAsync().GetAwaiter().GetResult();
        }

        protected string GetStagingDataPageUrl()
        {
            return IntegrationLinkHelper.GetStagingDataPage(CurrentIntegration);
        }

        protected string GetManualSendPageUrl()
        {
            return IntegrationLinkHelper.GetManualSendPage(CurrentIntegration);
        }

        protected IActionResult RedirectToStagingData()
        {
            return RedirectToPage(GetStagingDataPageUrl());
        }

        protected bool IsRouteBased => !string.IsNullOrWhiteSpace(Integration);

        /// Gets the route values that preserve the current integration context.
        /// Use when redirecting to maintain the integration in the URL.
        protected object GetRouteValuesWithIntegration(object? additionalValues = null)
        {
            if (!IsRouteBased || CurrentIntegration == null)
                return additionalValues ?? new { };

            // Merge Integration with additional values
            var dict = new Dictionary<string, object?>
            {
                ["Integration"] = CurrentIntegration.ToString()
            };

            if (additionalValues != null)
            {
                foreach (var prop in additionalValues.GetType().GetProperties())
                {
                    dict[prop.Name] = prop.GetValue(additionalValues);
                }
            }

            return dict;
        }

        /// Redirects to the current page with preserved integration context.
        /// Use this for POST-Redirect-GET pattern in route-based pages.
        protected IActionResult RedirectToSelf(object? queryParameters = null)
        {
            return RedirectToPage(GetRouteValuesWithIntegration(queryParameters));
        }

        #region TempData Messaging Helpers

        protected const string TempDataSuccessKey = "SuccessMessage";

        protected const string TempDataErrorKey = "ErrorMessage";

        protected const string TempDataInfoKey = "InfoMessage";

        protected void SetSuccessMessage(string message)
        {
            TempData[TempDataSuccessKey] = message;
        }

        protected void SetErrorMessage(string message)
        {
            TempData[TempDataErrorKey] = message;
        }

        protected void SetInfoMessage(string message)
        {
            TempData[TempDataInfoKey] = message;
        }

        public string? SuccessMessage => TempData[TempDataSuccessKey] as string;

        public string? ErrorMessage => TempData[TempDataErrorKey] as string;

        public string? InfoMessage => TempData[TempDataInfoKey] as string;

        #endregion
    }
}
