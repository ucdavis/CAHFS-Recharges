using CAHFS_Recharges.Models;

namespace CAHFS_Recharges.Services
{
    public interface IIntegrationContextService
    {
        IntegrationType? Resolve(HttpContext httpContext);

        ProductFamily? ResolveProductFamily(HttpContext httpContext);

        void SetIntegrationCookie(HttpContext httpContext, IntegrationType integration);

        void SetProductFamilyCookie(HttpContext httpContext, ProductFamily productFamily);

        void ClearIntegrationCookie(HttpContext httpContext);

        void ClearProductFamilyCookie(HttpContext httpContext);
    }
}
