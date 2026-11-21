using DeepHarness.Backend;
using DeepHarness.HistoryProvider;
using Microsoft.Extensions.DependencyInjection;

namespace DeepHarness;

public static class DeepHarnessServiceCollectionExtensions
{
    public static IServiceCollection AddDeepHarness(this IServiceCollection services, DeepHarnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionStringMessages))
        {
            throw new ArgumentException("Connection string messages is required.", nameof(options));
        }

        if (options.ConfigureHistoryDb is null)
        {
            throw new InvalidOperationException("History DB configuration is required for DeepHarness registration.");
        }

        if (options.ConfigureStoreDb is null)
        {
            throw new InvalidOperationException("Store DB configuration is required for DeepHarness registration.");
        }

        services.AddPooledDbContextFactory<ChatHistoryDbContext>(options.ConfigureHistoryDb ?? throw new InvalidOperationException("History DB configuration is required for DeepHarness registration."));
        services.AddPooledDbContextFactory<SqlStoreDbContext>(options.ConfigureStoreDb ?? throw new InvalidOperationException("Store DB configuration is required for DeepHarness registration."));

        services.AddSingleton<DbChatHistoryProvider>();

        return services;
    }
}
