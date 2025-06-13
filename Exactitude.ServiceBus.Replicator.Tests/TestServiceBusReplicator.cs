using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Exactitude.ServiceBus.Replicator.Models;
using Exactitude.ServiceBus.Replicator.Services;
using Moq;

namespace Exactitude.ServiceBus.Replicator.Tests;

public class TestServiceBusReplicator : ServiceBusReplicator
{
    private readonly Mock<ServiceBusSender> _senderMock;
    private readonly bool _shouldThrowError;
    private readonly bool _shouldThrowTransientError;
    private readonly bool _shouldDeadLetter;

    public TestServiceBusReplicator(
        Mock<ILogger<ServiceBusReplicator>> loggerMock,
        Mock<ServiceBusClient> sourceClientMock,
        Mock<ServiceBusClient> targetClientMock,
        Mock<ServiceBusAdministrationClient> sourceAdminClientMock,
        Mock<ServiceBusSender> senderMock,
        ServiceBusReplicatorConfig config,
        bool shouldThrowError = false,
        bool shouldThrowTransientError = false,
        bool shouldDeadLetter = false)
        : base(loggerMock.Object, new OptionsWrapper<ServiceBusReplicatorConfig>(config), sourceClientMock.Object, targetClientMock.Object, sourceAdminClientMock.Object)
    {
        _senderMock = senderMock;
        _shouldThrowError = shouldThrowError;
        _shouldThrowTransientError = shouldThrowTransientError;
        _shouldDeadLetter = shouldDeadLetter;
    }

    public async Task ProcessMessageAsync(ServiceBusReceivedMessage message, string topicName)
    {
        await ProcessMessageHandler(message, topicName);
    }

    protected override async Task ProcessMessageHandler(ServiceBusReceivedMessage message, string topicName)
    {
        if (_shouldThrowError)
        {
            throw new ServiceBusException("Test error", ServiceBusFailureReason.ServiceBusy);
        }

        if (_shouldThrowTransientError)
        {
            _logger.LogWarning(new ServiceBusException("Service busy", ServiceBusFailureReason.ServiceBusy),
                "Temporary service busy error while processing message from {Topic}", topicName);
            throw new ServiceBusException("Service busy", ServiceBusFailureReason.ServiceBusy);
        }

        if (_shouldDeadLetter)
        {
            _logger.LogError(new ServiceBusException("Message lock lost", ServiceBusFailureReason.MessageLockLost),
                "Error processing message from {Topic}", topicName);
            throw new ServiceBusException("Message lock lost", ServiceBusFailureReason.MessageLockLost);
        }

        await base.ProcessMessageHandler(message, topicName);
    }

    public async Task ProcessErrorAsync(Exception exception, string topicName)
    {
        await Task.Run(() => _logger.LogError(exception,
            "Error processing messages for topic {Topic}: {Error}",
            topicName,
            exception.Message));
    }

    protected override async Task<IEnumerable<string>> GetSourceTopicsAsync(CancellationToken cancellationToken)
    {
        return await base.GetSourceTopicsAsync(cancellationToken);
    }

    protected override async Task EnsureSubscriptionExistsAsync(string topicName, CancellationToken cancellationToken)
    {
        await base.EnsureSubscriptionExistsAsync(topicName, cancellationToken);
    }
} 