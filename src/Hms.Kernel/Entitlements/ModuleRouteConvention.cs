using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Kernel.Entitlements;

/// <summary>
/// Binds a route prefix to a licensed module, so every page under <c>/hr</c> carries the entitlement
/// check whether or not its author remembered to ask for one (ADR-0026).
/// <para>
/// This is the difference between an enforcement mechanism and an enforcement <em>habit</em>. A
/// per-page attribute is forgotten on the ninetieth screen; a convention applied at composition is
/// not, and the accompanying architecture test proves no module route escaped it.
/// </para>
/// </summary>
public sealed class ModuleRouteConvention(IReadOnlyDictionary<string, string> prefixToModule)
    : IPageApplicationModelConvention
{
    public void Apply(PageApplicationModel model)
    {
        var route = "/" + model.ViewEnginePath.TrimStart('/');

        foreach (var (prefix, module) in prefixToModule)
        {
            if (!route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            model.Filters.Add(new AuthorizeFilter(ModuleEntitlementPolicy.Name(module)));
            return;
        }
    }
}

public static class ModuleEntitlementExtensions
{
    /// <summary>
    /// Registers the entitlement handler and binds route prefixes to modules. Call once per host
    /// with that host's module map — the ERP maps every module it ships, the HRM SKU maps one.
    /// </summary>
    public static IServiceCollection AddModuleEntitlement(
        this IServiceCollection services, IReadOnlyDictionary<string, string> prefixToModule)
    {
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
            ModuleEntitlementHandler>();
        services.Configure<Microsoft.AspNetCore.Mvc.RazorPages.RazorPagesOptions>(o =>
            o.Conventions.Add(new ModuleRouteConvention(prefixToModule)));
        return services;
    }
}
