using Exactitude.ServiceBus.Replicator.Extensions;
using Exactitude.ServiceBus.Replicator.Services;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Add Azure Key Vault configuration only in non-test environments
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(builder.Configuration["AzureKeyVault:VaultUri"]!),
                new Azure.Identity.DefaultAzureCredential());
        }

        // Add Service Bus Replicator
        builder.Services.AddServiceBusReplicator(builder.Configuration, builder.Environment);

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        // Start the replicator service
        var replicator = app.Services.GetRequiredService<ServiceBusReplicator>();
        await replicator.StartAsync();

        await app.RunAsync();
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
