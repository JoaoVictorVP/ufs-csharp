using System;

namespace ufs.web;

public static class ServiceExtensions
{
    public static IServiceCollection AddUfsUtilities(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<IWebFsProvider, DefaultWebFsProvider>();
        return services;
    }
}
