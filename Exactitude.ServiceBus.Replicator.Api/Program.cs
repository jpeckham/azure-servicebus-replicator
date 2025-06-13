using Exactitude.ServiceBus.Replicator.Extensions;
using Exactitude.ServiceBus.Replicator.Services;
using Azure.Identity;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add Key Vault configuration (required for SAS connection strings)
        var keyVaultUri = builder.Configuration["AzureKeyVault:VaultUri"] 
            ?? throw new InvalidOperationException("Key Vault URI not configured");
        
        builder.Configuration.AddAzureKeyVault(
            new Uri(keyVaultUri),
            new DefaultAzureCredential());

        // Add services to the container.
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Add Service Bus Replicator
        builder.Services.AddServiceBusReplicator(builder.Configuration, builder.Environment);

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthorization();
        app.MapControllers();

        // Start the replicator service
        var replicator = app.Services.GetRequiredService<ServiceBusReplicator>();
        await replicator.StartAsync();

        await app.RunAsync();
    }
}
