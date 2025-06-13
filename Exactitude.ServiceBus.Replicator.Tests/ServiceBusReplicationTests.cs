using Azure.Messaging.ServiceBus;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace Exactitude.ServiceBus.Replicator.Tests
{
    [TestClass]
    public class ServiceBusReplicationTests
    {
        private IConfiguration _configuration = null!;
        private ServiceBusClient _sourceClient = null!;
        private ServiceBusClient _targetClient = null!;

        [TestInitialize]
        public void Initialize()
        {
            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var uniqueId = _configuration["AzureServiceBus:UniqueId"];
            var sourceNamespaceName = $"sb-central-{uniqueId}.servicebus.windows.net";
            var targetNamespaceName = $"sb-east2-{uniqueId}.servicebus.windows.net";

            var credential = new DefaultAzureCredential();
            _sourceClient = new ServiceBusClient(sourceNamespaceName, credential);
            _targetClient = new ServiceBusClient(targetNamespaceName, credential);
        }

        [TestMethod]
        public async Task TestMessageReplication()
        {
            var topicName = "test-topic";
            var messageId = Guid.NewGuid().ToString();

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

            await sender.SendMessageAsync(message);

            // Wait for replication
            await Task.Delay(TimeSpan.FromSeconds(10));

            // Check the target topic for the replicated message
            await using var receiver = _targetClient.CreateReceiver(topicName, "replicationapi");
            var receivedMessage = await receiver.ReceiveMessageAsync();

            Assert.IsNotNull(receivedMessage, "Message was not replicated to the target topic.");
            Assert.AreEqual(messageId, receivedMessage.MessageId, "Message ID does not match.");
        }
    }
}