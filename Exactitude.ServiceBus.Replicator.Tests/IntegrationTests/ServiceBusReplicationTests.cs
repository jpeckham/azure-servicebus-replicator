using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;

namespace Exactitude.ServiceBus.Replicator.Tests.IntegrationTests
{
    [TestClass]
    public class ServiceBusReplicationTests
    {
        private IConfiguration? _configuration;
        private SecretClient? _keyVaultClient;
        private ServiceBusClient? _sourceClient;
        private ServiceBusClient? _targetClient;
        private ServiceBusAdministrationClient? _sourceAdminClient;
        private ServiceBusAdministrationClient? _targetAdminClient;
        private readonly string _logFilePath = "logs/integration-tests.log";

        private void Log(string message)
        {
            var timestamp = DateTime.UtcNow.ToString("O");
            var logMessage = $"[{timestamp}] {message}";
            Console.WriteLine(logMessage);
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
        }

        [TestInitialize]
        public async Task Initialize()
        {
            Log("Starting test initialization...");
            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var keyVaultUri = new Uri(_configuration["AzureKeyVault:VaultUri"]);
            Log($"Using Key Vault URI: {keyVaultUri}");
            
            var credential = new DefaultAzureCredential();
            _keyVaultClient = new SecretClient(keyVaultUri, credential);

            var sourceConnectionString = await _keyVaultClient.GetSecretAsync("AzureServiceBus-ConnectionString-Value");
            var targetConnectionString = await _keyVaultClient.GetSecretAsync("AzureServiceBus-ConnectionString2-Value");
            Log("Retrieved connection strings from Key Vault");

            _sourceClient = new ServiceBusClient(sourceConnectionString.Value.Value);
            _targetClient = new ServiceBusClient(targetConnectionString.Value.Value);
            _sourceAdminClient = new ServiceBusAdministrationClient(sourceConnectionString.Value.Value);
            _targetAdminClient = new ServiceBusAdministrationClient(targetConnectionString.Value.Value);
            Log("Initialized Service Bus clients");
        }

        private void VerifyClientsInitialized()
        {
            if (_sourceClient == null) throw new InvalidOperationException("Source Service Bus client not initialized");
            if (_targetClient == null) throw new InvalidOperationException("Target Service Bus client not initialized");
            if (_sourceAdminClient == null) throw new InvalidOperationException("Source Service Bus admin client not initialized");
            if (_targetAdminClient == null) throw new InvalidOperationException("Target Service Bus admin client not initialized");
            if (_keyVaultClient == null) throw new InvalidOperationException("Key Vault client not initialized");
            if (_configuration == null) throw new InvalidOperationException("Configuration not initialized");
        }

        private async Task VerifyInfrastructure()
        {
            VerifyClientsInitialized();
            Log("Verifying infrastructure...");

            try
            {
                // Verify source topic exists
                var sourceTopic = await _sourceAdminClient.GetTopicAsync("test-topic");
                Log("Source topic 'test-topic' exists");

                // Verify target topic exists
                var targetTopic = await _targetAdminClient.GetTopicAsync("test-topic");
                Log("Target topic 'test-topic' exists");

                // Verify target subscription exists
                var targetSubscription = await _targetAdminClient.GetSubscriptionAsync("test-topic", "replicationapi");
                Log("Target subscription 'replicationapi' exists");

                Log("Infrastructure verification completed successfully");
            }
            catch (ServiceBusException ex)
            {
                Log($"Infrastructure verification failed: {ex.Message}");
                throw new AssertFailedException("Infrastructure verification failed - required topics or subscriptions are missing", ex);
            }
        }

        private async Task VerifyApiHealth()
        {
            try 
            {
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync("http://localhost:5000/health");
                if (response.IsSuccessStatusCode)
                {
                    Log("API health check successful");
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                Log($"API health check failed: Unexpected status code {response.StatusCode}. Response: {content}");
                throw new AssertFailedException("API health check failed - API returned unhealthy status");
            }
            catch (HttpRequestException ex)
            {
                Log($"API health check failed: {ex.Message}");
                throw new AssertFailedException($"API health check failed - API is not running or not accessible", ex);
            }
        }

        [TestMethod]
        public async Task TestMessageReplication()
        {            // Verify API is running
            Log("Verifying API health...");
            await VerifyApiHealth();

            // Then verify infrastructure
            await VerifyInfrastructure();

            var topicName = "test-topic";
            var messageId = Guid.NewGuid().ToString();
            Log($"Starting message replication test with message ID: {messageId}");

            // Send a message to the source topic
            await using var sender = _sourceClient.CreateSender(topicName);
            var message = new ServiceBusMessage("Test message for replication")
            {
                ContentType = "text/plain",
                Subject = "Test Message",
                MessageId = messageId,
                ApplicationProperties =
                {
                    { "MessageType", "Test" },
                    { "Priority", "High" },
                    { "Environment", "Development" }
                }
            };

            Log($"Sending message to source topic '{topicName}' with ID: {messageId}");
            await sender.SendMessageAsync(message);
            Log("Message sent successfully to source topic");

            // Wait for replication
            Log("Waiting for replication (10 seconds)...");
            await Task.Delay(TimeSpan.FromSeconds(10));
            Log("Replication wait period completed");

            // Check the target topic for the replicated message
            Log($"Attempting to receive message from target topic '{topicName}' subscription 'replicationapi'");
            await using var receiver = _targetClient.CreateReceiver(topicName, "replicationapi");
            var receivedMessage = await receiver.ReceiveMessageAsync();

            if (receivedMessage != null)
            {
                Log($"Message received from target topic. Message ID: {receivedMessage.MessageId}");
                Log($"Message content: {receivedMessage.Body}");
                Log($"Message properties: {string.Join(", ", receivedMessage.ApplicationProperties.Select(p => $"{p.Key}={p.Value}"))}");
                Assert.AreEqual(messageId, receivedMessage.MessageId, "Message ID does not match.");
            }
            else
            {
                Log("No message was received from the target topic.");
                Log("This could indicate:");
                Log("1. The message was not replicated");
                Log("2. The subscription name 'replicationapi' is incorrect");
                Log("3. The message was already consumed");
                Assert.Fail("Message was not replicated to the target topic.");
            }
        }
    }
}