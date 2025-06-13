using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Identity;
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
        IEnumerable<ServiceBusClient> clientList,
        ServiceBusAdministrationClient sourceAdminClient)
    {
        _logger = logger;
        _config = config.Value;
        
        // Message operation clients (using SAS auth)
        _sourceClient = clientList.ToArray()[0];
        _targetClient = clientList.ToArray()[1];
        
        // Admin client (using Azure AD auth)
        _sourceAdminClient = sourceAdminClient;

        _processors = new List<ServiceBusProcessor>();
        _topicProcessors = new Dictionary<string, ServiceBusProcessor>();
        
        _logger.LogInformation("ServiceBusReplicator initialized with hybrid auth: Azure AD for admin, SAS for messaging");
    }

    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {            
            _logger.LogInformation("Starting service bus replication");
            var topics = await GetSourceTopicsAsync(cancellationToken);
            foreach (var topic in topics)
            {
                _logger.LogInformation("Processing topic: {Topic}", topic);
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
        try
        {
            var topicNames = new List<string>();
            var topicProperties = _sourceAdminClient.GetTopicsAsync(cancellationToken);
            await foreach (var topicProperty in topicProperties)
            {
                topicNames.Add(topicProperty.Name);
            }
            _logger.LogInformation("Successfully retrieved {Count} topics from source namespace", topicNames.Count);
            return topicNames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve topics from source namespace");
            throw;
        }
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

            var rule = new CreateRuleOptions("ReplicationFilter", new SqlRuleFilter("replicated IS NULL"))
            {
                Action = new SqlRuleAction("SET replicated = 1")
            };

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
                var ruleOptions = new CreateRuleOptions("ReplicationFilter", new SqlRuleFilter("replicated IS NULL"))
                {
                    Action = new SqlRuleAction("SET replicated = 1")
                };
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
        var processor = _sourceClient.CreateProcessor(
            topicName,
            _config.Replication.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 1,
                AutoCompleteMessages = true
            });

        processor.ProcessMessageAsync += ProcessMessageEventHandler;
        processor.ProcessErrorAsync += ProcessErrorHandler;

        _topicProcessors[topicName] = processor;
        _processors.Add(processor);

        await processor.StartProcessingAsync(cancellationToken);
        _logger.LogInformation("Started processing messages for topic {Topic}", topicName);
    }

    private async Task ProcessMessageEventHandler(ProcessMessageEventArgs args)
    {
        try
        {
            await ProcessMessageHandler(args.Message, GetTopicNameFromEntityPath(args.EntityPath));
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
    private string GetTopicNameFromEntityPath(string entityPath)
    {
        // Entity path format: "topic-name/subscriptions/subscription-name"
        return entityPath.Split('/')[0];
    }

    protected virtual async Task ProcessMessageHandler(ServiceBusReceivedMessage message, string topicName)
    {
        var remainingTtl = GetRemainingTTL(message);
        ServiceBusSender? sender = null;

        try
        {
            sender = _targetClient.CreateSender(topicName);
            var messageClone = new ServiceBusMessage(message);
            messageClone.TimeToLive = remainingTtl;

            _logger.LogInformation(
                "Forwarding message {MessageId} from topic {Topic} with TTL {TTL}",
                message.MessageId ?? "(no id)",
                topicName,
                remainingTtl);

            await sender.SendMessageAsync(messageClone);

            _logger.LogDebug(
                "Successfully forwarded message {MessageId} to topic {Topic}",
                message.MessageId ?? "(no id)",
                topicName);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceBusy)
        {
            _logger.LogWarning(ex,
                "Temporary error processing message {MessageId} from topic {Topic} - will retry",
                message.MessageId ?? "(no id)",
                topicName);
            throw; // Rethrow to allow retry
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex,
                "Authentication failed while processing message {MessageId} from topic {Topic}. Verify SAS connection string is valid.",
                message.MessageId ?? "(no id)",
                topicName);
            throw; // Critical auth error, don't retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing message {MessageId} from topic {Topic}",
                message.MessageId ?? "(no id)",
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
        var expiresAt = message.ExpiresAt;
        var now = DateTimeOffset.UtcNow;
        var ttl = expiresAt > now ? expiresAt - now : TimeSpan.Zero;
        return ttl;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors)
        {
            await processor.DisposeAsync();
        }
    }
}