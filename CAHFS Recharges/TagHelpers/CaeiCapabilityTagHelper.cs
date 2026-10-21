using CAHFS_Recharges.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace CAHFS_Recharges.TagHelpers
{
    /// Suppresses markup when the current user lacks the named capability for the active product family.
    [HtmlTargetElement(Attributes = "caei-capability")]
    public sealed class CaeiCapabilityTagHelper : TagHelper
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICaeiCapabilityService _capabilityService;

        public CaeiCapabilityTagHelper(
            IHttpContextAccessor httpContextAccessor,
            ICaeiCapabilityService capabilityService)
        {
            _httpContextAccessor = httpContextAccessor;
            _capabilityService = capabilityService;
        }

        [HtmlAttributeName("caei-capability")]
        public string? Capability { get; set; }

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = null!;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (string.IsNullOrWhiteSpace(Capability))
                return;

            var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var family = ViewContext.ViewData["AuthorizedProductFamily"] as string
                ?? ViewContext.ViewData["ProductFamily"] as string
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(family))
            {
                output.SuppressOutput();
                return;
            }

            if (!_capabilityService.HasCapability(username, family, Capability))
                output.SuppressOutput();

            output.Attributes.RemoveAll("caei-capability");
        }
    }
}
