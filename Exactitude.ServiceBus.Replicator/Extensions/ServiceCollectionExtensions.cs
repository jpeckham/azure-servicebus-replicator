using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Services;

namespace Exactitude.ServiceBus.Replicator.Extensions;

public static class ServiceCollectionExtensions
{
    private const string MockConnectionString = "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mock-key";
    private const string MockConnectionString2 = "Endpoint=sb://test-namespace-2.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mock-key-2";

    public static IServiceCollection AddServiceBusReplicator(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        services.Configure<ServiceBusReplicatorConfig>(configuration);

        var config = configuration.Get<ServiceBusReplicatorConfig>()
            ?? throw new InvalidOperationException("Failed to bind ServiceBusReplicatorConfig from configuration");

        string sourceConnectionString;
        string targetConnectionString;

        // Check if we're in a test environment
        if (environment?.IsEnvironment("Testing") ?? false)
        {
            // In test environment, use the mock connection strings
            sourceConnectionString = MockConnectionString;
            targetConnectionString = MockConnectionString2;
        }
        else if (!string.IsNullOrEmpty(config.AzureServiceBus.ConnectionString.Value) &&
                 !string.IsNullOrEmpty(config.AzureServiceBus.ConnectionString2.Value))
        {
            // Use direct connection strings if provided
            sourceConnectionString = config.AzureServiceBus.ConnectionString.Value;
            targetConnectionString = config.AzureServiceBus.ConnectionString2.Value;

            // Validate connection strings
            if (string.IsNullOrEmpty(sourceConnectionString))
            {
                throw new InvalidOperationException("Source connection string is required");
            }
            if (string.IsNullOrEmpty(targetConnectionString))
            {
                throw new InvalidOperationException("Target connection string is required");
            }
        }
        else
        {
            // Get connection strings from Key Vault
            var credential = new DefaultAzureCredential();
            var keyVaultUri = new Uri(config.AzureKeyVault.VaultUri);
            var secretClient = new Azure.Security.KeyVault.Secrets.SecretClient(keyVaultUri, credential);

            sourceConnectionString = secretClient.GetSecret(config.AzureServiceBus.ConnectionString.Key).Value.Value;
            targetConnectionString = secretClient.GetSecret(config.AzureServiceBus.ConnectionString2.Key).Value.Value;

            // Validate connection strings
            if (string.IsNullOrEmpty(sourceConnectionString))
            {
                throw new InvalidOperationException($"Failed to get source connection string from Key Vault with key: {config.AzureServiceBus.ConnectionString.Key}");
            }
            if (string.IsNullOrEmpty(targetConnectionString))
            {
                throw new InvalidOperationException($"Failed to get target connection string from Key Vault with key: {config.AzureServiceBus.ConnectionString2.Key}");
            }
        }

        // Register Service Bus clients
        services.AddSingleton(new ServiceBusClient(sourceConnectionString));
        services.AddSingleton(new ServiceBusClient(targetConnectionString));
        services.AddSingleton(new ServiceBusAdministrationClient(sourceConnectionString));

        // Register the replicator service
        services.AddSingleton<ServiceBusReplicator>();

        return services;
    }
} 