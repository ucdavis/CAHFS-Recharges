using CAHFS_Recharges.Models;
using CAHFS_Recharges.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CAHFS_Recharges.Authorization
{
    /// Global Razor Pages filter: Layer 0 app allowlist + Layers 1–2 workspace gates.
    /// Does not modify operational page-model handlers.
    public sealed class WorkspaceAccessPageFilter : IAsyncPageFilter
    {
        private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/Login",
            "/Logout",
            "/CasLogin",
            "/Error",
            "/Denied",
            "/AccessDenied",
            "/Index"
        };

        private readonly ICaeiRoleResolver _roleResolver;
        private readonly ICaeiCapabilityService _capabilityService;
        private readonly IIntegrationContextService _integrationContext;

        public WorkspaceAccessPageFilter(
            ICaeiRoleResolver roleResolver,
            ICaeiCapabilityService capabilityService,
            IIntegrationContextService integrationContext)
        {
            _roleResolver = roleResolver;
            _capabilityService = capabilityService;
            _integrationContext = integrationContext;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) =>
            Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context,
            PageHandlerExecutionDelegate next)
        {
            var http = context.HttpContext;
            var path = http.Request.Path.Value ?? string.Empty;
            var pathBase = http.Request.PathBase.Value ?? string.Empty;

            if (ShouldSkip(path, pathBase))
            {
                await next();
                return;
            }

            var username = http.User?.Identity?.Name;
            var role = _roleResolver.TryResolveRole(username);
            if (role == null)
            {
                context.Result = RedirectAccessDenied(pathBase, "app");
                return;
            }

            var isHome = IsHome(path, pathBase);
            if (isHome)
            {
                if (IsSelectIntegration(context))
                {
                    var family = http.Request.Form["family"].ToString();
                    var lab = http.Request.Form["integration"].ToString();
                    if (!ProductFamilyExtensions.TryParse(family, out var familyEnum)
                        || !IntegrationTypeExtensions.TryParse(lab, out var labEnum)
                        || !_capabilityService.CanOpenWorkspace(username, labEnum.ToString(), familyEnum.ToString()))
                    {
                        context.Result = RedirectAccessDenied(pathBase, "workspace");
                        return;
                    }
                }

                await next();
                return;
            }

            if (!path.Contains("/Integrations/", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/Integrations", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (!TryResolveLab(http, out var labName))
            {
                context.Result = RedirectAccessDenied(pathBase, "workspace");
                return;
            }

            if (!TryResolveFamily(http, out var familyName))
            {
                context.Result = RedirectAccessDenied(pathBase, "workspace");
                return;
            }

            if (!_capabilityService.IsInProductFamily(username, familyName)
                || !_capabilityService.IsInLab(username, labName))
            {
                context.Result = RedirectAccessDenied(pathBase, "workspace");
                return;
            }

            var requiredCapability = ResolveRequiredCapability(http, path);
            if (!_capabilityService.HasCapability(username, familyName, requiredCapability))
            {
                context.Result = RedirectAccessDenied(pathBase, "capability");
                return;
            }

            if (!_capabilityService.HasCapability(username, familyName, CaeiCapabilities.Read))
            {
                context.Result = RedirectAccessDenied(pathBase, "capability");
                return;
            }

            if (context.HandlerInstance is PageModel pageModel)
            {
                pageModel.ViewData["Capabilities"] = _capabilityService.GetCapabilities(username, familyName);
                pageModel.ViewData["IntegrationAuthorized"] = true;
                pageModel.ViewData["AuthorizedProductFamily"] = familyName;
            }

            await next();
        }

        private static bool ShouldSkip(string path, string pathBase)
        {
            var relative = path;
            if (!string.IsNullOrEmpty(pathBase) && path.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
                relative = path[pathBase.Length..];

            if (string.IsNullOrEmpty(relative))
                relative = "/";

            foreach (var skip in SkipPaths)
            {
                if (relative.Equals(skip, StringComparison.OrdinalIgnoreCase)
                    || relative.Equals(skip + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsHome(string path, string pathBase)
        {
            var relative = path;
            if (!string.IsNullOrEmpty(pathBase) && path.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
                relative = path[pathBase.Length..];

            return relative.Equals("/Home", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("/Home/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSelectIntegration(PageHandlerExecutingContext context)
        {
            var name = context.HandlerMethod?.Name;
            return string.Equals(name, "OnPostSelectIntegration", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "OnPostSelectIntegrationAsync", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveLab(HttpContext http, out string labName)
        {
            labName = string.Empty;
            var resolved = _integrationContext.Resolve(http);
            if (resolved.HasValue)
            {
                labName = resolved.Value.ToString();
                return true;
            }

            if (http.Request.RouteValues.TryGetValue("integration", out var routeVal)
                && IntegrationTypeExtensions.TryParse(routeVal?.ToString(), out var fromRoute))
            {
                labName = fromRoute.ToString();
                return true;
            }

            return false;
        }

        private bool TryResolveFamily(HttpContext http, out string familyName)
        {
            familyName = string.Empty;
            var resolved = _integrationContext.ResolveProductFamily(http);
            if (resolved.HasValue)
            {
                familyName = resolved.Value.ToString();
                return true;
            }

            var path = http.Request.Path.Value ?? string.Empty;
            if (path.Contains("/Hiwu/", StringComparison.OrdinalIgnoreCase))
            {
                familyName = nameof(ProductFamily.Hiwu);
                return true;
            }

            if (path.Contains("/Lockbox/", StringComparison.OrdinalIgnoreCase))
            {
                familyName = nameof(ProductFamily.Lockbox);
                return true;
            }

            if (path.Contains("/Staging/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/History/", StringComparison.OrdinalIgnoreCase))
            {
                familyName = nameof(ProductFamily.AE);
                return true;
            }

            return false;
        }

        private static string ResolveRequiredCapability(HttpContext http, string path)
        {
            if (!HttpMethods.IsPost(http.Request.Method))
                return CaeiCapabilities.Read;

            if (path.Contains("/SendToAggieEnterprise", StringComparison.OrdinalIgnoreCase))
                return CaeiCapabilities.PostBatch;

            if (path.Contains("/CoaValidation", StringComparison.OrdinalIgnoreCase))
                return CaeiCapabilities.Validate;

            return CaeiCapabilities.Mutate;
        }

        private static IActionResult RedirectAccessDenied(string pathBase, string reason)
        {
            var url = (pathBase.TrimEnd('/') + "/AccessDenied?reason=" + Uri.EscapeDataString(reason));
            return new RedirectResult(url);
        }
    }
}
