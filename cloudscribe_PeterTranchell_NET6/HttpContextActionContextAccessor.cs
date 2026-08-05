using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;

namespace cloudscribe_PeterTranchell_NET6
{
    /// <summary>
    /// Provides an implementation of IActionContextAccessor backed by IHttpContextAccessor.
    /// This avoids depending on the framework's ActionContextAccessor concrete type which
    /// may be marked obsolete in newer framework versions.
    /// </summary>
    public class HttpContextActionContextAccessor : IActionContextAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextActionContextAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public ActionContext ActionContext
        {
            get
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null) return null;

                if (httpContext.Items.TryGetValue(typeof(ActionContext), out var stored) && stored is ActionContext ac)
                {
                    return ac;
                }

                // Try to obtain RouteData from the HttpContext; fall back to an empty RouteData if none.
                var routeData = httpContext.GetRouteData() ?? new RouteData();

                return new ActionContext(httpContext, routeData, new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            }
            set
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    httpContext.Items[typeof(ActionContext)] = value;
                }
            }
        }
    }
}
