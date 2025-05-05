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
public class ServiceBusReplicatorTests
{
    private Mock<ILogger<ServiceBusReplicator>> _loggerMock = null!;
    private Mock<ServiceBusClient> _sourceClientMock = null!;
    private Mock<ServiceBusClient> _targetClientMock = null!;
    private Mock<ServiceBusAdministrationClient> _sourceAdminClientMock = null!;
    private Mock<ServiceBusProcessor> _processorMock = null!;
    private Mock<ServiceBusSender> _senderMock = null!;
    private Mock<ServiceBusReceiver> _receiverMock = null!;
    private ServiceBusReplicatorConfig _config = null!;
    private TestServiceBusReplicator _replicator = null!;

    [TestInitialize]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<ServiceBusReplicator>>();
        _sourceClientMock = new Mock<ServiceBusClient>();
        _targetClientMock = new Mock<ServiceBusClient>();
        _sourceAdminClientMock = new Mock<ServiceBusAdministrationClient>();
        _processorMock = new Mock<ServiceBusProcessor>();
        _senderMock = new Mock<ServiceBusSender>();
        _receiverMock = new Mock<ServiceBusReceiver>();

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

        _sourceClientMock
            .Setup(x => x.CreateProcessor(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ServiceBusProcessorOptions>()))
            .Returns(_processorMock.Object);

        _sourceAdminClientMock
            .Setup(x => x.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .Returns(new AsyncPageableMock<TopicProperties>(Array.Empty<TopicProperties>()));
    }

    [TestMethod]
    public async Task StartAsync_ShouldCreateSubscriptionsForAllTopics()
    {
        // Arrange
        var topics = new[] { "topic1", "topic2" }.Select(name => TestHelpers.CreateTopicProperties(name));
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

        var processorMocks = new Dictionary<string, Mock<ServiceBusProcessor>>();
        _sourceClientMock
            .Setup(x => x.CreateProcessor(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ServiceBusProcessorOptions>()))
            .Returns<string, string, ServiceBusProcessorOptions>((topic, _, __) =>
            {
                if (!processorMocks.ContainsKey(topic))
                {
                    var processorMock = new Mock<ServiceBusProcessor>();
                    processorMock
                        .Setup(x => x.StartProcessingAsync(It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);
                    processorMocks[topic] = processorMock;
                }
                return processorMocks[topic].Object;
            });

        // Act
        await _replicator.StartAsync();

        // Assert
        _sourceAdminClientMock.Verify(
            x => x.CreateSubscriptionAsync(
                It.IsAny<CreateSubscriptionOptions>(),
                It.IsAny<CreateRuleOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        foreach (var processorMock in processorMocks.Values)
        {
            processorMock.Verify(
                x => x.StartProcessingAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestMethod]
    public async Task StartAsync_HandlesSubscriptionCleanupAndRuleUpdates()
    {
        // Arrange
        var topics = new[] { "topic1" }.Select(name => TestHelpers.CreateTopicProperties(name));
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
        _sourceAdminClientMock.Verify(
            x => x.DeleteRuleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "$Default",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _sourceAdminClientMock.Verify(
            x => x.CreateRuleAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<CreateRuleOptions>(o =>
                    o.Name == "ReplicationFilter" &&
                    o.Filter.GetType() == typeof(SqlRuleFilter) &&
                    ((SqlRuleFilter)o.Filter).SqlExpression == "replicated IS NULL"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        processorMock.Verify(
            x => x.StartProcessingAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class AsyncPageableMock<T> : AsyncPageable<T> where T : notnull
{
    private readonly IEnumerable<T> _items;

    public AsyncPageableMock(IEnumerable<T> items)
    {
        _items = items;
    }

    public override async IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
    {
        yield return Azure.Page<T>.FromValues(_items.ToList(), null, Mock.Of<Response>());
    }
} 