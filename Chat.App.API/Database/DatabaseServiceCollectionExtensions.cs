using Microsoft.EntityFrameworkCore;

namespace Chat.App.API.Database;

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentNullException("Database connection string is required. Set 'ConnectionStrings:Default' or 'ConnectionString' in configuration.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IConversationRepository, ConversationRepository>();

        var provider = services.BuildServiceProvider();
        var environment = provider.GetRequiredService<IHostEnvironment>();

        if (environment.IsDevelopment())
        {
            using var scope = provider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (dbContext.Database.GetMigrations().Any())
            {
                dbContext.Database.Migrate();
            }
            else
            {
                dbContext.Database.EnsureCreated();
            }
        }

        return services;
    }
}
