using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace Exactitude.ServiceBus.Replicator.Models;

public class ServiceBusReplicatorConfig
{
    public AzureServiceBusConfig AzureServiceBus { get; set; } = new();
    public AzureKeyVaultConfig AzureKeyVault { get; set; } = new();
    public ReplicationConfig Replication { get; set; } = new();
}

public class AzureServiceBusConfig
{
    [Required]
    public string UniqueId { get; set; } = string.Empty;
    public ConnectionStringConfig ConnectionString { get; set; } = new();
    public ConnectionStringConfig ConnectionString2 { get; set; } = new();
}

public class AzureKeyVaultConfig
{
    public string VaultUri { get; set; } = string.Empty;
}

public class ConnectionStringConfig
{
    public string Key { get; set; } = string.Empty;
}

public class ReplicationConfig
{
    public string SubscriptionName { get; set; } = "replicationapi";
    public int DefaultTTLMinutes { get; set; } = 10;
}