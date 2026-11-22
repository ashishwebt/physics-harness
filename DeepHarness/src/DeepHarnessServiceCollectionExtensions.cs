using DeepHarness.Backend;
using DeepHarness.HistoryProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DeepHarness;

public static class DeepHarnessServiceCollectionExtensions
{
    public static IServiceCollection AddDeepHarness(this IServiceCollection services, DeepHarnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddPooledDbContextFactory<ChatHistoryDbContext>(options.ConfigureHistoryDb ?? (db =>
            db.UseInMemoryDatabase("DeepHarness.History")));
        services.AddPooledDbContextFactory<SqlStoreDbContext>(options.ConfigureStoreDb ?? (db =>
            db.UseInMemoryDatabase("DeepHarness.Store")));

        services.AddSingleton<DbChatHistoryProvider>();

        return services;
    }
}
