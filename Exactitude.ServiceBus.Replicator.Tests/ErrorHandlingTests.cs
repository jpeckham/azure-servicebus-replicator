using Azure;
using Azure.Core;
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
public class ErrorHandlingTests
{
    private Mock<ILogger<ServiceBusReplicator>> _loggerMock = null!;
    private Mock<ServiceBusClient> _sourceClientMock = null!;
    private Mock<ServiceBusClient> _targetClientMock = null!;
    private Mock<ServiceBusAdministrationClient> _sourceAdminClientMock = null!;
    private Mock<ServiceBusSender> _senderMock = null!;
    private ServiceBusReplicatorConfig _config = null!;
    private TestServiceBusReplicator _replicator = null!;

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
    public async Task ProcessMessage_WhenSendFails_LogsError()
    {
        // Arrange
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("test"),
            enqueuedTime: DateTimeOffset.UtcNow,
            timeToLive: TimeSpan.FromMinutes(10));
        var topicName = "test-topic";
        var exception = new ServiceBusException("Test error", ServiceBusFailureReason.ServiceBusy);

        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

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
    public async Task ProcessError_LogsErrorDetails()
    {
        // Arrange
        var exception = new ServiceBusException("Test error", ServiceBusFailureReason.ServiceBusy);

        // Act
        await _replicator.ProcessErrorAsync(exception, "test-topic");

        // Assert
        _loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => true),
            It.Is<ServiceBusException>(e => e == exception),
            It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_WhenGetTopicsFails_LogsErrorAndThrows()
    {
        // Arrange
        var replicator = new TestServiceBusReplicator(
            _loggerMock,
            _sourceClientMock,
            _targetClientMock,
            _sourceAdminClientMock,
            _senderMock,
            _config);

        var exception = new ServiceBusException("Test error", ServiceBusFailureReason.ServiceTimeout);
        var emptyPageable = new AsyncPageableMock<TopicProperties>(Array.Empty<TopicProperties>());
        _sourceAdminClientMock
            .Setup(x => x.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .Returns(emptyPageable)
            .Callback(() => throw exception);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ServiceBusException>(
            async () => await replicator.StartAsync());

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [TestMethod]
    public async Task CreateSubscription_WhenRuleExists_HandlesGracefully()
    {
        // Arrange
        var topics = new[] { TestHelpers.CreateTopicProperties(
            "test-topic", 
            defaultMessageTimeToLive: TimeSpan.FromMinutes(10),
            autoDeleteOnIdle: TimeSpan.FromMinutes(5),
            duplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(1)) };
        var asyncPageable = new AsyncPageableMock<TopicProperties>(topics);

        _sourceAdminClientMock
            .Setup(x => x.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);

        _sourceAdminClientMock
            .Setup(x => x.SubscriptionExistsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        _sourceAdminClientMock
            .Setup(x => x.CreateRuleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<CreateRuleOptions>(o => 
                    o.Name == "ReplicationFilter" &&
                    o.Filter.GetType() == typeof(SqlRuleFilter) &&
                    ((SqlRuleFilter)o.Filter).SqlExpression == "replicated IS NULL"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Rule exists", ServiceBusFailureReason.MessagingEntityAlreadyExists));

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

        // Act & Assert
        await _replicator.StartAsync(); // Should not throw

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Never);
    }
} 