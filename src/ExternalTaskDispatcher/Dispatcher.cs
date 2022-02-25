namespace ExternalTaskDispatcher;

/// <summary>
/// Implementation of the external task dispatcher. It can be run as a .NET background service.
/// </summary>
public class Dispatcher : BackgroundService
{
    private DispatcherConfig _dispatcherConfig;
    private readonly ILogger<Dispatcher> _logger;
    private readonly IServiceTaskHandler _serviceTaskHandler;
    private readonly IMessageTaskHandler _messsageTaskHandler;
    private List<string> _topics;
    private CamundaClient _camundaClient;
    private DateTime? _lastErrorTimeStamp;
    private JsonSerializerSettings _serializerSettings;
    private DateTime _lastTopicCacheInvalidationTimestamp;

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="configuration">The .NET configuration.</param>
    public Dispatcher(
        ILogger<Dispatcher> logger, 
        IConfiguration configuration, 
        IServiceTaskHandler serviceTaskHandler, 
        IMessageTaskHandler messsageTaskHandler)
    {
        _logger = logger;
        this._serviceTaskHandler = serviceTaskHandler;
        this._messsageTaskHandler = messsageTaskHandler;
        _dispatcherConfig = DispatcherConfig.Build(configuration);
        _dispatcherConfig.Log(logger);
        _topics = _dispatcherConfig.Topics;
        _camundaClient = CamundaClient.Create(_dispatcherConfig.CamundaUrl);
        _serializerSettings = new JsonSerializerSettings
        {
        };
    }

    /// <summary>
    /// The entrypoint of the dispatcher.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for cancelling the background worker.</param>
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        FetchExternalTasks fetchInfo = new FetchExternalTasks
        {
            MaxTasks = 1,
            WorkerId = _dispatcherConfig.WorkerId,
            AsyncResponseTimeout = _dispatcherConfig.LongPollingIntervalInMs
        };

        // initialize topic cache invalidation timestamp
        _lastTopicCacheInvalidationTimestamp = DateTime.Now;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // discover new topics
                if (_dispatcherConfig.AutomaticTopicDiscovery)
                {
                    await DiscoverTopics();
                }

                // specify topics to fetch
                fetchInfo.Topics = _topics.Select(t => new FetchExternalTaskTopic(t, _dispatcherConfig.TaskLockDurationInMs)).ToList();

                // skip fetch & lock when no topics are known
                if (fetchInfo.Topics.Count == 0)
                {
                    _logger.LogDebug($"No topics known to fetch & lock. Delaying for 10 seconds...");
                    await Task.Delay(10000);
                    continue;
                }

                _logger.LogDebug($"Start fetch & lock");

                var lockedTasks = await _camundaClient.ExternalTasks.FetchAndLock(fetchInfo);
                _logger.LogDebug($"Found {lockedTasks.Count} tasks");
                foreach (var lockedTask in lockedTasks)
                {
                    await HandleExternalTask(lockedTask);
                }
            }
            catch (Exception ex)
            {
                var errorTimestamp = DateTime.Now;
                _logger.LogCritical(ex, $"Error while waiting for tasks");
                if (_lastErrorTimeStamp.HasValue)
                {
                    if (errorTimestamp.Subtract(_lastErrorTimeStamp.Value).TotalMilliseconds < 1000)
                    {
                        _logger.LogWarning($"Fast failure detected (within {errorTimestamp.Subtract(_lastErrorTimeStamp.Value).TotalMilliseconds}ms). Backing off for 10 seconds");
                        await Task.Delay((int)TimeSpan.FromSeconds(10).TotalMilliseconds);
                    }
                }
                _lastErrorTimeStamp = errorTimestamp;
            }
        }
    }

    /// <summary>
    /// Handle a Camunda external task.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    private async Task HandleExternalTask(LockedExternalTask lockedTask)
    {
        _logger.LogInformation($"Received external task for topic '{lockedTask.TopicName}' with Id '{lockedTask.Id}'.");

        // determine task type (Service, Message, Signal)
        var extTaskType = DetermineExternalTaskType(lockedTask.TopicName);
        _logger.LogDebug($"External task with Id '{lockedTask.Id}' has type '{extTaskType}'.");

        // skip unknown task types
        if (extTaskType == ExternalTaskType.Unknown)
        {
            string errorMessage = $"External task with Id '{lockedTask.Id}' has unknown task type. Failing task.";
            _logger.LogInformation(errorMessage);
            await HandleFailureAsync(lockedTask, errorMessage, 0);
            return;
        }

        try
        {
            // handle task
            var outputVariables = new Dictionary<string, VariableValue>();
            switch (extTaskType)
            {
                case ExternalTaskType.Service:
                    outputVariables = await _serviceTaskHandler.HandleServiceTaskAsync(lockedTask);
                    break;
                case ExternalTaskType.Message:
                    outputVariables = await _messsageTaskHandler.HandleMessageTaskAsync(lockedTask);
                    break;
            }
            
            // complete task
            var complete = new CompleteExternalTask
            {
                WorkerId = _dispatcherConfig.WorkerId,
                Variables = outputVariables
            };
            await _camundaClient.ExternalTasks[lockedTask.Id].Complete(complete);

            _logger.LogInformation($"Completed {lockedTask.TopicName} task with Id: {lockedTask.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during {lockedTask.TopicName} task with Id: {lockedTask.Id}");
            await HandleFailureAsync(lockedTask, ex);
        }
    }

    /// <summary>
    /// Discover any new topics used in Camunda process instances.
    /// </summary>
    private async Task DiscoverTopics()
    {
        _logger.LogDebug($"Start automatic topic discovery");

        // invalidate cache
        if (DateTime.Now.Subtract(_lastTopicCacheInvalidationTimestamp).Minutes >= _dispatcherConfig.TopicCacheInvalidationIntervalInMin)
        {
            _topics.Clear();
            _logger.LogDebug($"Topic cache invalidated.");
            _lastTopicCacheInvalidationTimestamp = DateTime.Now;
        }

        var tasks = await _camundaClient.ExternalTasks.Query().List();
        if (tasks.Count > 0)
        {
            foreach (var task in tasks)
            {
                if (!_topics.Contains(task.TopicName))
                {
                    _logger.LogDebug($"Discovered new topic {task.TopicName}.");
                    _topics.Add(task.TopicName);
                }
            }
        }
        else
        {
            _logger.LogDebug($"No new topics found");
        }
    }

    /// <summary>
    /// Determine the type of the external task.
    /// </summary>
    /// <param name="topicName">The name of the topic.</param>
    /// <returns>The <see cref="ExternalTaskType"/> representing the type of the task.</returns>
    /// <remarks>Camunda supports different task types that all work with the external task mechanism. These 
    /// are Service-, Message- and Signal-tasks. Each of these types work by specifying a topic-name for the 
    /// task. To differentiate between these types, the external task dispatcher expects a prefix in the topic-
    /// name. Which prefix maps to which task type can be specified in the configuration.</remarks>
    private ExternalTaskType DetermineExternalTaskType(string topicName)
    {
        if (topicName.StartsWith(_dispatcherConfig.ServiceTaskPrefix))
        {
            return ExternalTaskType.Service;
        }
        else if (topicName.StartsWith(_dispatcherConfig.MessageTaskPrefix))
        {
            return ExternalTaskType.Message;
        }
        return ExternalTaskType.Unknown;
    }

    /// <summary>
    /// Handle a failure during the execution of an external task.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <param name="errorMessage">The error message to include in the failure.</param>
    /// <param name="retries">The max amount of retries to do before actually failing.</param>
    private async Task HandleFailureAsync(LockedExternalTask lockedTask, string errorMessage, int retries = 5)
    {
        await HandleFailureAsync(lockedTask, errorMessage, string.Empty, retries);
    }

    /// <summary>
    /// Handle a failure during the execution of an external task.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <param name="ex">The exception to include in the failure.</param>
    /// <param name="retries">The max amount of retries to do before actually failing.</param>
    private async Task HandleFailureAsync(LockedExternalTask lockedTask, Exception ex, int retries = 5)
    {
        await HandleFailureAsync(lockedTask, ex.Message, ex.ToString(), retries);
    }

    /// <summary>
    /// Handle a failure during the execution of an external task.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <param name="errorMessage">The error message to include in the failure.</param>
    /// <param name="errorDetails">The error details to include in the failure.</param>
    /// <param name="retries">The max amount of retries to do before actually failing.</param>
    private async Task HandleFailureAsync(LockedExternalTask lockedTask, string errorMessage, string errorDetails, int retries = 5)
    {
        _logger.LogDebug($"HandleFailure for {lockedTask.TopicName} task with Id: {lockedTask.Id}");

        try
        {
            var failure = new ExternalTaskFailure
            {
                WorkerId = _dispatcherConfig.WorkerId,
                ErrorMessage = errorMessage,
                ErrorDetails = errorDetails,
                Retries = lockedTask.Retries != null ? lockedTask.Retries.Value - 1 : retries,
                RetryTimeout = 5000
            };
            await _camundaClient.ExternalTasks[lockedTask.Id].HandleFailure(failure);

        }
        catch (Exception handleEx)
        {
            _logger.LogError(handleEx, $"Error while handling failure for {lockedTask.TopicName} task with Id: {lockedTask.Id}");
        }
    }
}
