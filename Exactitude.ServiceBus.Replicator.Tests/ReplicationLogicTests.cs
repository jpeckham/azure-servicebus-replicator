using AutoFixture;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Exactitude.ServiceBus.Replicator.Tests;

[TestClass]
public class ReplicationLogicTests
{
    private readonly Fixture _fixture;
    private Mock<ILogger<ServiceBusReplicator>> _loggerMock = null!;
    private Mock<ServiceBusClient> _sourceClientMock = null!;
    private Mock<ServiceBusClient> _targetClientMock = null!;
    private Mock<ServiceBusAdministrationClient> _sourceAdminClientMock = null!;
    private Mock<ServiceBusSender> _senderMock = null!;
    private ServiceBusReplicatorConfig _config = null!;
    private TestServiceBusReplicator _replicator = null!;

    public ReplicationLogicTests()
    {
        _fixture = new Fixture();
    }

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<ServiceBusReplicator>>();
        _sourceClientMock = new Mock<ServiceBusClient>();
        _targetClientMock = new Mock<ServiceBusClient>();
        _sourceAdminClientMock = new Mock<ServiceBusAdministrationClient>();
        _senderMock = new Mock<ServiceBusSender>();

        _config = new ServiceBusReplicatorConfig
        {
            Replication = new ReplicationConfig
            {
                SubscriptionName = "replicationapi",
                DefaultTTLMinutes = 10
            }
        };

        _replicator = new TestServiceBusReplicator(
            _loggerMock,
            _sourceClientMock,
            _targetClientMock,
            _sourceAdminClientMock,
            _senderMock,
            _config);

        _targetClientMock
            .Setup(x => x.CreateSender(It.IsAny<string>()))
            .Returns(_senderMock.Object);
    }

    [TestMethod]
    public async Task ProcessMessage_PreservesMessageProperties()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid().ToString();
        var contentType = "application/json";
        var subject = "Test Message";
        var partitionKey = "test-partition";
        var replyTo = "reply-queue";
        var replyToSessionId = "reply-session";
        var sessionId = "test-session";
        var to = "destination";
        var enqueuedTime = DateTimeOffset.UtcNow;
        var timeToLive = TimeSpan.FromMinutes(30);
        var properties = new Dictionary<string, object>
        {
            { "CustomProperty1", "Value1" },
            { "CustomProperty2", 42 }
        };

        var message = TestHelpers.CreateServiceBusReceivedMessage(
            BinaryData.FromString("Test message body"),
            messageId,
            correlationId,
            contentType,
            subject,
            partitionKey,
            replyTo,
            replyToSessionId,
            sessionId,
            to,
            enqueuedTime,
            timeToLive,
            1,
            properties);

        ServiceBusMessage? capturedMessage = null;
        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        // Act
        await _replicator.ProcessMessageAsync(message, "test-topic");

        // Assert
        Assert.IsNotNull(capturedMessage);
        Assert.AreEqual(messageId, capturedMessage.MessageId);
        Assert.AreEqual(correlationId, capturedMessage.CorrelationId);
        Assert.AreEqual(contentType, capturedMessage.ContentType);
        Assert.AreEqual(subject, capturedMessage.Subject);
        Assert.AreEqual(partitionKey, capturedMessage.PartitionKey);
        Assert.AreEqual(replyTo, capturedMessage.ReplyTo);
        Assert.AreEqual(replyToSessionId, capturedMessage.ReplyToSessionId);
        Assert.AreEqual(sessionId, capturedMessage.SessionId);
        Assert.AreEqual(to, capturedMessage.To);

        foreach (var prop in properties)
        {
            Assert.IsTrue(capturedMessage.ApplicationProperties.ContainsKey(prop.Key));
            Assert.AreEqual(prop.Value, capturedMessage.ApplicationProperties[prop.Key]);
        }

        Assert.IsTrue(capturedMessage.ApplicationProperties.ContainsKey("replicated"));
        Assert.AreEqual(1, capturedMessage.ApplicationProperties["replicated"]);
    }

    [TestMethod]
    public async Task ProcessMessage_HandlesTransientError()
    {
        // Arrange
        var message = TestHelpers.CreateServiceBusReceivedMessage(
            BinaryData.FromString("Test message body"));

        var replicator = new TestServiceBusReplicator(
            _loggerMock,
            _sourceClientMock,
            _targetClientMock,
            _sourceAdminClientMock,
            _senderMock,
            _config,
            shouldThrowTransientError: true);

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<ServiceBusException>(
            async () => await replicator.ProcessMessageAsync(message, "test-topic"));

        Assert.AreEqual(ServiceBusFailureReason.ServiceBusy, exception.Reason);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.Is<ServiceBusException>(e => e.Reason == ServiceBusFailureReason.ServiceBusy),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessMessage_HandlesDeadLetter()
    {
        // Arrange
        var message = TestHelpers.CreateServiceBusReceivedMessage(
            BinaryData.FromString("Test message body"));

        var replicator = new TestServiceBusReplicator(
            _loggerMock,
            _sourceClientMock,
            _targetClientMock,
            _sourceAdminClientMock,
            _senderMock,
            _config,
            shouldDeadLetter: true);

        // Act & Assert
        var exception = await Assert.ThrowsExceptionAsync<ServiceBusException>(
            async () => await replicator.ProcessMessageAsync(message, "test-topic"));

        Assert.AreEqual(ServiceBusFailureReason.MessageLockLost, exception.Reason);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.Is<ServiceBusException>(e => e.Reason == ServiceBusFailureReason.MessageLockLost),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessMessage_CalculatesTTLCorrectly()
    {
        // Arrange
        var replicator = new TestServiceBusReplicator(
            _loggerMock,
            _sourceClientMock,
            _targetClientMock,
            _sourceAdminClientMock,
            _senderMock,
            _config);

        var originalTtl = TimeSpan.FromMinutes(30);
        var enqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("test"),
            enqueuedTime: enqueuedTime,
            timeToLive: originalTtl);

        ServiceBusMessage? capturedMessage = null;
        _senderMock
            .Setup(x => x.SendMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        // Act
        await replicator.ProcessMessageAsync(message, "test-topic");

        // Assert
        Assert.IsNotNull(capturedMessage);
        // Should be original TTL minus elapsed time (approximately 20 minutes)
        Assert.IsTrue(capturedMessage.TimeToLive > TimeSpan.FromMinutes(19));
        Assert.IsTrue(capturedMessage.TimeToLive < TimeSpan.FromMinutes(21));
    }

    [TestMethod]
    public async Task CreateSubscription_SetsCorrectFilter()
    {
        // Arrange
        var topicName = "test-topic";
        var subscriptionName = "replicationapi";
        var filterExpression = "replicated IS NULL";

        var topics = new[] { TestHelpers.CreateTopicProperties(topicName) };
        var asyncPageable = new AsyncPageableMock<TopicProperties>(topics);

        _sourceAdminClientMock
            .Setup(x => x.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);

        _sourceAdminClientMock
            .Setup(x => x.SubscriptionExistsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        var processorMock = new Mock<ServiceBusProcessor>();
        _sourceClientMock
            .Setup(x => x.CreateProcessor(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ServiceBusProcessorOptions>()))
            .Returns(processorMock.Object);

        processorMock
            .Setup(x => x.StartProcessingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _replicator.StartAsync();

        // Assert
        _sourceAdminClientMock.Verify(x => x.CreateSubscriptionAsync(
            It.Is<CreateSubscriptionOptions>(o =>
                o.TopicName == topicName &&
                o.SubscriptionName == subscriptionName &&
                o.MaxDeliveryCount == 10),
            It.Is<CreateRuleOptions>(r =>
                r.Name == "ReplicationFilter" &&
                r.Filter.GetType() == typeof(SqlRuleFilter) &&
                ((SqlRuleFilter)r.Filter).SqlExpression == filterExpression),
            It.IsAny<CancellationToken>()),
            Times.Once);

        processorMock.Verify(
            x => x.StartProcessingAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessMessage_HandlesNullAndEmptyProperties()
    {
        // Arrange
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("test-body"),
            messageId: null,
            correlationId: string.Empty,
            contentType: null,
            subject: string.Empty,
            partitionKey: null,
            replyTo: string.Empty,
            replyToSessionId: null,
            sessionId: string.Empty,
            to: null);

        ServiceBusMessage? capturedMessage = null;
        _senderMock.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((m, _) => capturedMessage = m)
            .Returns(Task.CompletedTask);

        // Act
        await _replicator.ProcessMessageAsync(message, "test-topic");

        // Assert
        _senderMock.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsNotNull(capturedMessage);
        
        // Verify null properties are preserved as null
        Assert.IsNull(capturedMessage.MessageId);
        Assert.IsNull(capturedMessage.ContentType);
        Assert.IsNull(capturedMessage.PartitionKey);
        Assert.IsNull(capturedMessage.ReplyToSessionId);
        Assert.IsNull(capturedMessage.To);

        // Verify empty strings are preserved as empty
        Assert.AreEqual(string.Empty, capturedMessage.CorrelationId);
        Assert.AreEqual(string.Empty, capturedMessage.Subject);
        Assert.AreEqual(string.Empty, capturedMessage.ReplyTo);
        Assert.AreEqual(string.Empty, capturedMessage.SessionId);

        // Verify replication properties are still added
        Assert.IsTrue(capturedMessage.ApplicationProperties.ContainsKey("replicated"));
        Assert.AreEqual(1, capturedMessage.ApplicationProperties["replicated"]);
    }

    [TestMethod]
    public async Task ProcessMessage_HandlesTTLEdgeCases()
    {
        // Arrange
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("test"),
            enqueuedTime: DateTimeOffset.UtcNow.AddMinutes(-1),
            timeToLive: TimeSpan.FromMinutes(5));
        var topicName = "test-topic";

        // Act
        await _replicator.ProcessMessageAsync(message, topicName);

        // Assert
        _senderMock.Verify(x => x.SendMessageAsync(
            It.Is<ServiceBusMessage>(m => m.TimeToLive.TotalMinutes >= 4),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessMessage_HandlesTransientFailuresWithRetries()
    {
        // Arrange
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("test"),
            enqueuedTime: DateTimeOffset.UtcNow,
            timeToLive: TimeSpan.FromMinutes(10));

        var topicName = "test-topic";
        var serviceBusyException = new ServiceBusException("Service busy", ServiceBusFailureReason.ServiceBusy);

        _senderMock
            .SetupSequence(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(serviceBusyException)
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ServiceBusException>(async () =>
            await _replicator.ProcessMessageAsync(message, topicName));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.Is<ServiceBusException>(e => e.Reason == ServiceBusFailureReason.ServiceBusy),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessMessage_HandlesDeadLettering()
    {
        // Arrange
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("test"),
            enqueuedTime: DateTimeOffset.UtcNow,
            timeToLive: TimeSpan.FromMinutes(10));

        var topicName = "test-topic";
        var exception = new ServiceBusException("Test error", ServiceBusFailureReason.MessageLockLost);

        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ServiceBusException>(async () =>
            await _replicator.ProcessMessageAsync(message, topicName));

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.Is<ServiceBusException>(e => e.Reason == ServiceBusFailureReason.MessageLockLost),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }
} 