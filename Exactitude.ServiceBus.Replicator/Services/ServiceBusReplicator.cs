using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Exactitude.ServiceBus.Replicator.Models;

namespace Exactitude.ServiceBus.Replicator.Services;

public class ServiceBusReplicator : IAsyncDisposable
{
    protected readonly ILogger<ServiceBusReplicator> _logger;
    protected readonly ServiceBusClient _sourceClient;
    protected readonly ServiceBusClient _targetClient;
    protected readonly ServiceBusAdministrationClient _sourceAdminClient;
    protected readonly ServiceBusReplicatorConfig _config;
    protected readonly List<ServiceBusProcessor> _processors;
    private readonly Dictionary<string, ServiceBusProcessor> _topicProcessors;

    public ServiceBusReplicator(
        ILogger<ServiceBusReplicator> logger,
        IOptions<ServiceBusReplicatorConfig> config,
        ServiceBusClient sourceClient,
        ServiceBusClient targetClient,
        ServiceBusAdministrationClient sourceAdminClient)
    {
        _logger = logger;
        _sourceClient = sourceClient;
        _targetClient = targetClient;
        _sourceAdminClient = sourceAdminClient;
        _config = config.Value;
        _processors = new List<ServiceBusProcessor>();
        _topicProcessors = new Dictionary<string, ServiceBusProcessor>();
    }

    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var topics = await GetSourceTopicsAsync(cancellationToken);
            foreach (var topic in topics)
            {
                await EnsureSubscriptionExistsAsync(topic, cancellationToken);
                await StartProcessingTopicAsync(topic, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service bus replication");
            throw;
        }
    }

    protected virtual async Task<IEnumerable<string>> GetSourceTopicsAsync(CancellationToken cancellationToken)
    {
        var topics = new List<string>();
        await foreach (var topic in _sourceAdminClient.GetTopicsAsync(cancellationToken))
        {
            topics.Add(topic.Name);
        }
        return topics;
    }

    protected virtual async Task EnsureSubscriptionExistsAsync(string topicName, CancellationToken cancellationToken)
    {
        var subscriptionName = _config.Replication.SubscriptionName;
        var exists = await _sourceAdminClient.SubscriptionExistsAsync(topicName, subscriptionName, cancellationToken);

        if (!exists)
        {
            var options = new CreateSubscriptionOptions(topicName, subscriptionName)
            {
                MaxDeliveryCount = 10,
                DefaultMessageTimeToLive = TimeSpan.FromMinutes(_config.Replication.DefaultTTLMinutes)
            };

            var rule = new CreateRuleOptions("ReplicationFilter", new SqlRuleFilter("replicated IS NULL"));
            rule.Action = new SqlRuleAction("SET replicated = 1");

            await _sourceAdminClient.CreateSubscriptionAsync(options, rule, cancellationToken);
            _logger.LogInformation("Created subscription {Subscription} for topic {Topic}", subscriptionName, topicName);
        }
        else
        {
            // Ensure the default rule is removed and our filter is in place
            try
            {
                await _sourceAdminClient.DeleteRuleAsync(topicName, subscriptionName, "$Default", cancellationToken);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
            {
                // Rule doesn't exist, that's fine
            }

            try
            {
                var ruleOptions = new CreateRuleOptions("ReplicationFilter", new SqlRuleFilter("replicated IS NULL"));
                ruleOptions.Action = new SqlRuleAction("SET replicated = 1");
                await _sourceAdminClient.CreateRuleAsync(topicName, subscriptionName, ruleOptions, cancellationToken);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
            {
                // Rule already exists, that's fine
            }
        }
    }

    private async Task StartProcessingTopicAsync(string topicName, CancellationToken cancellationToken)
    {
        if (_topicProcessors.ContainsKey(topicName))
        {
            _logger.LogInformation("Processor already exists for topic {Topic}", topicName);
            return;
        }

        try
        {
            var subscriptionName = _config.Replication.SubscriptionName;
            var processor = _sourceClient.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 1,
                AutoCompleteMessages = false
            });

            processor.ProcessMessageAsync += ProcessMessageEventHandler;
            processor.ProcessErrorAsync += ProcessErrorHandler;

            await processor.StartProcessingAsync(cancellationToken);
            _processors.Add(processor);
            _topicProcessors[topicName] = processor;
            _logger.LogInformation("Started processing messages for topic {Topic}", topicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting processing for topic {Topic}", topicName);
            throw;
        }
    }

    private async Task ProcessMessageEventHandler(ProcessMessageEventArgs args)
    {
        try
        {
            await ProcessMessageHandler(args.Message, args.EntityPath);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceBusy)
        {
            _logger.LogWarning(ex, "Temporary service busy error while processing message from {Topic}", args.EntityPath);
            // Don't rethrow transient errors - let the processor retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Topic}", args.EntityPath);
            throw;
        }
    }

    private Task ProcessErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error processing messages from {Topic}: {Error}", args.EntityPath, args.Exception.Message);
        return Task.CompletedTask;
    }

    protected virtual async Task ProcessMessageHandler(ServiceBusReceivedMessage message, string topicName)
    {
        ServiceBusSender? sender = null;
        try
        {
            sender = _targetClient.CreateSender(topicName);

            var newMessage = new ServiceBusMessage(message)
            {
                TimeToLive = GetRemainingTTL(message)
            };

            newMessage.ApplicationProperties.Add("replicated", 1);
            newMessage.ApplicationProperties.Add("repl-origin", message.MessageId);
            newMessage.ApplicationProperties.Add("repl-enqueue-time", message.EnqueuedTime);
            newMessage.ApplicationProperties.Add("repl-sequence", message.SequenceNumber);

            await sender.SendMessageAsync(newMessage);

            _logger.LogInformation(
                "Replicated message {MessageId} from topic {SourceTopic} to {TargetTopic}",
                message.MessageId,
                topicName,
                topicName);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceBusy)
        {
            _logger.LogWarning(ex,
                "Temporary error processing message {MessageId} from topic {Topic}",
                message.MessageId,
                topicName);
            throw; // Rethrow to allow retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing message {MessageId} from topic {Topic}",
                message.MessageId,
                topicName);
            throw;
        }
        finally
        {
            if (sender != null)
            {
                await sender.DisposeAsync();
            }
        }
    }

    protected virtual TimeSpan GetRemainingTTL(ServiceBusReceivedMessage message)
    {
        var originalTTL = message.TimeToLive;
        if (originalTTL == TimeSpan.MaxValue)
        {
            return TimeSpan.FromMinutes(_config.Replication.DefaultTTLMinutes);
        }

        var elapsedTime = DateTimeOffset.UtcNow - message.EnqueuedTime;
        var remainingTTL = originalTTL - elapsedTime;

        // Ensure minimum TTL of 4 minutes
        if (remainingTTL.TotalMinutes < 4)
        {
            return TimeSpan.FromMinutes(4);
        }

        return remainingTTL;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
        {
            processor.ProcessMessageAsync -= ProcessMessageEventHandler;
            processor.ProcessErrorAsync -= ProcessErrorHandler;
            await processor.DisposeAsync();
        }
        _processors.Clear();
        _topicProcessors.Clear();
    }
} 