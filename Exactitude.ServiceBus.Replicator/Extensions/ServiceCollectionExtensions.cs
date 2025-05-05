using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Services;

namespace Exactitude.ServiceBus.Replicator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceBusReplicator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusReplicatorConfig>(configuration);

        var config = configuration.Get<ServiceBusReplicatorConfig>()
            ?? throw new InvalidOperationException("Failed to bind ServiceBusReplicatorConfig from configuration");

        // Get connection strings from Key Vault
        var credential = new DefaultAzureCredential();
        var keyVaultUri = new Uri(config.AzureKeyVault.VaultUri);
        var secretClient = new Azure.Security.KeyVault.Secrets.SecretClient(keyVaultUri, credential);

        var sourceConnectionString = secretClient.GetSecret(config.AzureServiceBus.ConnectionString.Key).Value.Value;
        var targetConnectionString = secretClient.GetSecret(config.AzureServiceBus.ConnectionString2.Key).Value.Value;

        // Register Service Bus clients
        services.AddSingleton(new ServiceBusClient(sourceConnectionString));
        services.AddSingleton(new ServiceBusClient(targetConnectionString));
        services.AddSingleton(new ServiceBusAdministrationClient(sourceConnectionString));

        // Register the replicator service
        services.AddSingleton<ServiceBusReplicator>();

        return services;
    }
} 