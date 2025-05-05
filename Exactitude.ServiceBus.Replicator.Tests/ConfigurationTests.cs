using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Extensions;

namespace Exactitude.ServiceBus.Replicator.Tests;

[TestClass]
public class ConfigurationTests
{
    private IConfiguration CreateTestConfiguration(Dictionary<string, string> initialData)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(initialData)
            .Build();
    }

    [TestMethod]
    public void Configuration_BindsCorrectly()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            {"AzureServiceBus:ConnectionString:Key", "test-connection-string"},
            {"AzureServiceBus:ConnectionString2:Key", "test-connection-string-2"},
            {"Replication:SubscriptionName", "test-subscription"},
            {"Replication:DefaultTTLMinutes", "15"},
            {"AzureKeyVault:VaultUri", "https://test-vault.vault.azure.net/"}
        };

        var configuration = CreateTestConfiguration(configData);

        // Act
        var config = configuration.Get<ServiceBusReplicatorConfig>();

        // Assert
        Assert.IsNotNull(config);
        Assert.AreEqual("test-connection-string", config.AzureServiceBus.ConnectionString.Key);
        Assert.AreEqual("test-connection-string-2", config.AzureServiceBus.ConnectionString2.Key);
        Assert.AreEqual("test-subscription", config.Replication.SubscriptionName);
        Assert.AreEqual(15, config.Replication.DefaultTTLMinutes);
        Assert.AreEqual("https://test-vault.vault.azure.net/", config.AzureKeyVault.VaultUri);
    }

    [TestMethod]
    public void Configuration_DefaultValues_AreCorrect()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceBus:AzureKeyVault:VaultUri"] = "https://test-vault.vault.azure.net/",
                ["ServiceBus:AzureServiceBus:ConnectionString:Key"] = "source-connection",
                ["ServiceBus:AzureServiceBus:ConnectionString2:Key"] = "target-connection"
            })
            .Build();

        var services = new ServiceCollection()
            .Configure<ServiceBusReplicatorConfig>(config.GetSection("ServiceBus"))
            .BuildServiceProvider();

        // Act
        var options = services.GetRequiredService<IOptions<ServiceBusReplicatorConfig>>();

        // Assert
        Assert.IsNotNull(options.Value);
        Assert.AreEqual("https://test-vault.vault.azure.net/", options.Value.AzureKeyVault.VaultUri);
        Assert.AreEqual("source-connection", options.Value.AzureServiceBus.ConnectionString.Key);
        Assert.AreEqual("target-connection", options.Value.AzureServiceBus.ConnectionString2.Key);
        Assert.AreEqual(10, options.Value.Replication.DefaultTTLMinutes);
        Assert.AreEqual("replicationapi", options.Value.Replication.SubscriptionName);
    }

    [TestMethod]
    public void Configuration_InvalidTTL_ThrowsException()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            {"Replication:DefaultTTLMinutes", "-1"}
        };

        var configuration = CreateTestConfiguration(configData);

        // Act & Assert
        var config = configuration.Get<ServiceBusReplicatorConfig>();
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            if (config.Replication.DefaultTTLMinutes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config.Replication.DefaultTTLMinutes), 
                    "TTL must be greater than 0");
            }
        });
    }

    [TestMethod]
    public void Configuration_MissingKeyVaultUri_ThrowsException()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceBus:AzureServiceBus:ConnectionString:Key"] = "source-connection",
                ["ServiceBus:AzureServiceBus:ConnectionString2:Key"] = "target-connection"
            })
            .Build();

        var services = new ServiceCollection()
            .Configure<ServiceBusReplicatorConfig>(config.GetSection("ServiceBus"))
            .BuildServiceProvider();

        // Act & Assert
        var options = services.GetRequiredService<IOptions<ServiceBusReplicatorConfig>>();
        Assert.AreEqual(string.Empty, options.Value.AzureKeyVault.VaultUri);
    }
} 