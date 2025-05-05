using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using System.Collections.Generic;

namespace Exactitude.ServiceBus.Replicator.Tests;

public static class TestHelpers
{
    public static ServiceBusReceivedMessage CreateServiceBusReceivedMessage(
        BinaryData body,
        string? messageId = null,
        string? correlationId = null,
        string? contentType = null,
        string? subject = null,
        string? partitionKey = null,
        string? replyTo = null,
        string? replyToSessionId = null,
        string? sessionId = null,
        string? to = null,
        DateTimeOffset? enqueuedTime = null,
        TimeSpan? timeToLive = null,
        int deliveryCount = 1,
        IDictionary<string, object>? properties = null)
    {
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body,
            messageId: messageId,
            correlationId: correlationId,
            contentType: contentType,
            subject: subject,
            partitionKey: partitionKey,
            replyTo: replyTo,
            replyToSessionId: replyToSessionId,
            sessionId: sessionId,
            to: to,
            enqueuedTime: enqueuedTime ?? DateTimeOffset.UtcNow,
            timeToLive: timeToLive ?? TimeSpan.FromDays(1),
            deliveryCount: deliveryCount,
            properties: properties);
    }

    public static TopicProperties CreateTopicProperties(
        string name,
        long maxSizeInMegabytes = 1024,
        TimeSpan? defaultMessageTimeToLive = null,
        TimeSpan? autoDeleteOnIdle = null,
        bool enableBatchedOperations = true,
        bool requiresDuplicateDetection = true,
        TimeSpan? duplicateDetectionHistoryTimeWindow = null,
        bool enablePartitioning = false,
        EntityStatus? status = null)
    {
        return ServiceBusModelFactory.TopicProperties(
            name,
            maxSizeInMegabytes,
            requiresDuplicateDetection,
            defaultMessageTimeToLive ?? TimeSpan.FromDays(1),
            autoDeleteOnIdle ?? TimeSpan.FromDays(7),
            duplicateDetectionHistoryTimeWindow ?? TimeSpan.FromMinutes(10),
            enableBatchedOperations,
            status ?? EntityStatus.Active,
            enablePartitioning);
    }

    public static SubscriptionProperties CreateSubscriptionProperties(
        string topicName,
        string subscriptionName,
        TimeSpan? lockDuration = null,
        bool requiresSession = false,
        TimeSpan? defaultMessageTimeToLive = null,
        TimeSpan? autoDeleteOnIdle = null,
        bool deadLetteringOnMessageExpiration = true,
        int maxDeliveryCount = 10,
        bool enableBatchedOperations = true,
        string? forwardTo = null,
        string? forwardDeadLetteredMessagesTo = null,
        string? userMetadata = null,
        EntityStatus? status = null)
    {
        return ServiceBusModelFactory.SubscriptionProperties(
            topicName,
            subscriptionName,
            lockDuration ?? TimeSpan.FromMinutes(5),
            requiresSession,
            defaultMessageTimeToLive ?? TimeSpan.FromDays(1),
            autoDeleteOnIdle ?? TimeSpan.FromDays(7),
            deadLetteringOnMessageExpiration,
            maxDeliveryCount,
            enableBatchedOperations,
            status ?? EntityStatus.Active,
            forwardTo,
            forwardDeadLetteredMessagesTo,
            userMetadata);
    }

    public static RuleProperties CreateRuleProperties(
        string ruleName,
        SqlRuleFilter filter,
        RuleAction? action = null)
    {
        return ServiceBusModelFactory.RuleProperties(
            ruleName,
            filter,
            action);
    }
} 