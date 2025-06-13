using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Services;
using Azure.Identity;

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

        // Get the SAS connection string values from configuration (populated from Key Vault)
        var connectionStringKey = config.AzureServiceBus?.ConnectionString?.Key
            ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString:Key is not configured");
        var connectionString2Key = config.AzureServiceBus?.ConnectionString2?.Key
            ?? throw new InvalidOperationException("AzureServiceBus:ConnectionString2:Key is not configured");

        // Get the actual connection strings from configuration, populated through Key Vault
        var connectionString = configuration[connectionStringKey]
            ?? throw new InvalidOperationException($"Connection string for key '{connectionStringKey}' not found");
        var connectionString2 = configuration[connectionString2Key]
            ?? throw new InvalidOperationException($"Connection string for key '{connectionString2Key}' not found");

        // Configure client options for messaging operations
        var clientOptions = new ServiceBusClientOptions
        {
            TransportType = ServiceBusTransportType.AmqpWebSockets
        };

        // Extract the namespace from the connection string for the admin client
        var namespaceUri = GetNamespaceFromConnectionString(connectionString);
        if (string.IsNullOrEmpty(namespaceUri))
        {
            throw new InvalidOperationException("Could not extract namespace from connection string");
        }

        // Create Service Bus clients with the appropriate authentication:
        // 1. Message operation clients with SAS auth (using connection strings)
        services.AddSingleton(sp => new ServiceBusClient(connectionString, clientOptions));   // Source
        services.AddSingleton(sp => new ServiceBusClient(connectionString2, clientOptions));  // Target

        // 2. Admin client with Azure AD auth (using DefaultAzureCredential)
        services.AddSingleton(new ServiceBusAdministrationClient(
            namespaceUri,
            new DefaultAzureCredential()));

        // Add the replicator service
        services.AddTransient<ServiceBusReplicator>();

        return services;
    }

    /// <summary>
    /// Extracts the namespace URI from a Service Bus connection string
    /// </summary>
    private static string GetNamespaceFromConnectionString(string connectionString)
    {
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
            {
                var endpoint = part.Split('=')[1];
                // Remove sb:// prefix if present
                return endpoint.Replace("sb://", string.Empty).TrimEnd('/');
            }
        }
        return string.Empty;
    }
}