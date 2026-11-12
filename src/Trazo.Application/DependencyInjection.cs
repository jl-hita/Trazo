using Microsoft.Extensions.DependencyInjection;

namespace Trazo.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application use cases here as the training domain is introduced.
        return services;
    }
}
