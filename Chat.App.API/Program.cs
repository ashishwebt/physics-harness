using Chat.App.API.AgentServices;
using Chat.App.API.Database;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        var connectionStringConversation = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Database connection string is required.");

        builder.Services.AddDatabaseServices(connectionStringConversation);

        var agentApiKey = builder.Configuration["Agent:ApiKey"] ?? string.Empty;
        var agentModel = builder.Configuration["Agent:Model"] ?? string.Empty;
        var agentName = builder.Configuration["Agent:Name"] ?? string.Empty;
        var agentSystemPrompt = builder.Configuration["Agent:SystemPrompt"] ?? string.Empty;
        var connectionStringMessages = builder.Configuration.GetConnectionString("ConnectionStringMessages")
            ?? throw new InvalidOperationException(
                "Database connection string is required.");

        builder.Services.AddAgentServices(
            agentApiKey,
            agentModel,
            agentName,
            agentSystemPrompt,
            connectionStringMessages);

        builder.Services.AddCors(options =>
        {
            var origins =
                builder.Configuration
                    .GetSection("Cors:AllowedOrigins")
                    .Get<string[]>()
                ?? ["*"];

            options.AddPolicy("ChatAppCors", policy =>
            {
                if (origins.Contains("*"))
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                }
                else
                {
                    policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
                }
            });
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "v1");
            });
        }

        app.UseHttpsRedirection();
        app.UseCors("ChatAppCors");

        app.MapControllers();

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "Chat.App API is running."
        }));

        app.Run();

    }
}