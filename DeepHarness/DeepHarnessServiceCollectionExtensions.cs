using DeepHarness.Backend;
using DeepHarness.HistoryProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        services.AddPooledDbContextFactory<ChatHistoryDbContext>(dbContextOptions =>
        {
            dbContextOptions.UseSqlite(options.ConnectionStringMessages);
        });

        services.AddPooledDbContextFactory<SqlStoreDbContext>(dbContextOptions =>
        {
            dbContextOptions.UseSqlite(options.ConnectionStringMessages);
        });

        services.AddSingleton<DbChatHistoryProvider>();

        var provider = services.BuildServiceProvider();
        var environment = provider.GetService<IHostEnvironment>();

        if (options.EnsureDatabaseCreatedOnStartup && environment is not null && environment.IsDevelopment())
        {
            using var scope = provider.CreateScope();
            var historyDbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ChatHistoryDbContext>>();
            using (var dbContext = historyDbFactory.CreateDbContext())
            {
                if (dbContext.Database.GetMigrations().Any())
                {
                    dbContext.Database.Migrate();
                }
                else
                {
                    dbContext.Database.EnsureCreated();
                }
            }

            var storeDbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SqlStoreDbContext>>();
            using (var dbContext = storeDbFactory.CreateDbContext())
            {
                if (dbContext.Database.GetMigrations().Any())
                {
                    dbContext.Database.Migrate();
                }
                else
                {
                    dbContext.Database.EnsureCreated();
                }
            }
        }

        return services;
    }
}
