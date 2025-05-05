using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Services;
using Exactitude.ServiceBus.Replicator.Api;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Security.KeyVault.Secrets;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Exactitude.ServiceBus.Replicator.Tests;

[TestClass]
public class ApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing"); // Use Testing environment to skip Key Vault
                
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    // Clear any existing configuration sources
                    config.Sources.Clear();

                    // Add our test configuration
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AzureServiceBus:ConnectionString:Value"] = "test-connection-string",
                        ["AzureServiceBus:ConnectionString2:Value"] = "test-connection-string-2",
                        ["AzureServiceBus:ConnectionString:Key"] = "test-connection-string-key",
                        ["AzureServiceBus:ConnectionString2:Key"] = "test-connection-string-2-key",
                        ["Replication:SubscriptionName"] = "replicationapi",
                        ["Replication:DefaultTTLMinutes"] = "10",
                        ["AzureKeyVault:VaultUri"] = "https://test-key-vault.vault.azure.net/"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove the real ServiceBusReplicator registration
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ServiceBusReplicator));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Mock the Key Vault client
                    var secretClientMock = new Mock<SecretClient>();
                    var mockResponse1 = new Mock<Azure.Response<KeyVaultSecret>>();
                    mockResponse1.Setup(r => r.Value).Returns(new KeyVaultSecret("test-connection-string-key", "test-connection-string"));
                    var mockResponse2 = new Mock<Azure.Response<KeyVaultSecret>>();
                    mockResponse2.Setup(r => r.Value).Returns(new KeyVaultSecret("test-connection-string-2-key", "test-connection-string-2"));

                    secretClientMock.Setup(x => x.GetSecret("test-connection-string-key", null, default))
                        .Returns(mockResponse1.Object);
                    secretClientMock.Setup(x => x.GetSecret("test-connection-string-2-key", null, default))
                        .Returns(mockResponse2.Object);

                    services.AddSingleton<SecretClient>(secretClientMock.Object);

                    // Add a mock ServiceBusReplicator with correct constructor parameter order
                    services.AddSingleton<ServiceBusReplicator>(sp =>
                    {
                        var configMock = new Mock<IOptions<ServiceBusReplicatorConfig>>();
                        configMock.Setup(x => x.Value).Returns(new ServiceBusReplicatorConfig
                        {
                            Replication = new ReplicationConfig
                            {
                                SubscriptionName = "replicationapi",
                                DefaultTTLMinutes = 10
                            }
                        });

                        var mock = new Mock<ServiceBusReplicator>(
                            Mock.Of<ILogger<ServiceBusReplicator>>(),
                            configMock.Object,
                            Mock.Of<ServiceBusClient>(),
                            Mock.Of<ServiceBusClient>(),
                            Mock.Of<ServiceBusAdministrationClient>());

                        mock.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

                        return mock.Object;
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_client != null)
        {
            _client.Dispose();
        }
        if (_factory != null)
        {
            _factory.Dispose();
        }
    }

    [TestMethod]
    public async Task HealthEndpoint_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.IsNotNull(content);
        Assert.IsTrue(content.ContainsKey("status"));
        Assert.AreEqual("healthy", content["status"]);
    }

    [TestMethod]
    public async Task SwaggerEndpoint_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void ServiceBusReplicator_IsRegistered()
    {
        // Arrange
        var serviceProvider = _factory.Services;

        // Act
        var replicator = serviceProvider.GetService<ServiceBusReplicator>();

        // Assert
        Assert.IsNotNull(replicator);
    }

    [TestMethod]
    public void Configuration_IsCorrectlyBound()
    {
        // Arrange
        var serviceProvider = _factory.Services;
        var config = serviceProvider.GetRequiredService<IConfiguration>();

        // Act
        var replicatorConfig = new ServiceBusReplicatorConfig();
        config.Bind(replicatorConfig);

        // Assert
        Assert.IsNotNull(replicatorConfig);
        Assert.AreEqual("test-connection-string", config["AzureServiceBus:ConnectionString:Value"]);
        Assert.AreEqual("test-connection-string-2", config["AzureServiceBus:ConnectionString2:Value"]);
        Assert.AreEqual("replicationapi", replicatorConfig.Replication.SubscriptionName);
        Assert.AreEqual(10, replicatorConfig.Replication.DefaultTTLMinutes);
        Assert.AreEqual("https://test-key-vault.vault.azure.net/", replicatorConfig.AzureKeyVault.VaultUri);
    }
} 