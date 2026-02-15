using CAHFS_Recharges.Models;

namespace CAHFS_Recharges.Services
{
    public interface IIntegrationContextService
    {
        IntegrationType? Resolve(HttpContext httpContext);

        void SetIntegrationCookie(HttpContext httpContext, IntegrationType integration);

        void ClearIntegrationCookie(HttpContext httpContext);
    }
}
