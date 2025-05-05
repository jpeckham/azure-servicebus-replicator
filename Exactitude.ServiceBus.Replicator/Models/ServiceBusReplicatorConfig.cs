namespace Exactitude.ServiceBus.Replicator.Models;

public class ServiceBusReplicatorConfig
{
    public ServiceBusConnectionConfig AzureServiceBus { get; set; } = new();
    public ReplicationConfig Replication { get; set; } = new();
    public KeyVaultConfig AzureKeyVault { get; set; } = new();
}

public class ServiceBusConnectionConfig
{
    public ConnectionStringConfig ConnectionString { get; set; } = new();
    public ConnectionStringConfig ConnectionString2 { get; set; } = new();
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

public class KeyVaultConfig
{
    public string VaultUri { get; set; } = string.Empty;
} 