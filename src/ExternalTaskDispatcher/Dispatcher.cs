namespace ExternalTaskDispatcher;

/// <summary>
/// Implementation of the external task dispatcher. It can be run as a .NET background service.
/// </summary>
public class Dispatcher : BackgroundService
{
    private DispatcherConfig _dispatcherConfig;
    private readonly ILogger<Dispatcher> _logger;
    private readonly IHttpClientFactory _clientFactory;
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
    /// <param name="httpClientFactory">The factory for safelay creating HTTPClient instances.</param>
    public Dispatcher(ILogger<Dispatcher> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _clientFactory = httpClientFactory;
        _dispatcherConfig = ReadConfig(configuration);
        _topics = _dispatcherConfig.Topics;
        _camundaClient = CamundaClient.Create(_dispatcherConfig.CamundaUrl);
        _serializerSettings = new JsonSerializerSettings
        {
        };
    }

    /// <summary>
    /// Read thesettings for the dispatcher from the configuration.
    /// </summary>
    /// <param name="configuration">The .NET configuration.</param>
    /// <returns>An initialized <see cref="DispatcherConfig"/> instance.</returns>
    private DispatcherConfig ReadConfig(IConfiguration configuration)
    {
        var dispatcherConfig = new DispatcherConfig();
        configuration.GetSection("Dispatcher").Bind(dispatcherConfig);

        //_apimKey = GetAPIMKey().Result ?? dispatcherConfig.APIMKey;

        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"Configuration:");
        logBuilder.AppendLine(new string('-', 35));
        logBuilder.AppendLine($"Worker Id: {dispatcherConfig.WorkerId}");
        logBuilder.AppendLine($"Camunda Url: {dispatcherConfig.CamundaUrl}");
        logBuilder.AppendLine($"APIM Url: {dispatcherConfig.APIMUrl}");
        logBuilder.AppendLine($"APIM Key: {new string('*', dispatcherConfig.APIMKey.Length)}");
        logBuilder.AppendLine($"Pre-configured Topics:");
        foreach (string topic in dispatcherConfig.Topics)
        {
            logBuilder.AppendLine($"- {topic}");
        }
        logBuilder.AppendLine($"Automatic topic discovery: {dispatcherConfig.AutomaticTopicDiscovery}");
        logBuilder.AppendLine($"Long polling interval in ms: {dispatcherConfig.LongPollingIntervalInMs}");
        logBuilder.AppendLine($"Task lock duration in ms: {dispatcherConfig.TaskLockDurationInMs}");
        logBuilder.AppendLine($"Topic cache invalidation interval in minutes: {dispatcherConfig.TopicCacheInvalidationIntervalInMin}");

        _logger.LogInformation(logBuilder.ToString());

        return dispatcherConfig;
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
                    await HandleTask(lockedTask);
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
    private async Task HandleTask(LockedExternalTask lockedTask)
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

        // skip unsupported task types
        if (extTaskType == ExternalTaskType.Message || extTaskType == ExternalTaskType.Signal)
        {
            string errorMessage = $"External task with Id '{lockedTask.Id}' has unsupported task type '{extTaskType}'. Failing task.";
            _logger.LogInformation(errorMessage);
            await HandleFailureAsync(lockedTask, errorMessage, 0);
            return;
        }

        // handle task
        try
        {
            var outputVariables = await HandleServiceTaskAsync(lockedTask);
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
            _logger.LogInformation($"Topic cache invalidated.");
            _lastTopicCacheInvalidationTimestamp = DateTime.Now;
        }

        var tasks = await _camundaClient.ExternalTasks.Query().List();
        if (tasks.Count > 0)
        {
            foreach (var task in tasks)
            {
                if (!_topics.Contains(task.TopicName))
                {
                    _logger.LogInformation($"Discovered new topic {task.TopicName}.");
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
        else if (topicName.StartsWith(_dispatcherConfig.SignalTaskPrefix))
        {
            return ExternalTaskType.Signal;
        }
        return ExternalTaskType.Unknown;
    }

    /// <summary>
    /// Handle an external task of type ServiceTask.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <returns>A dictionary with the variables returned from the external task implementation.</returns>
    private async Task<Dictionary<string, VariableValue>> HandleServiceTaskAsync(LockedExternalTask lockedTask)
    {
        _logger.LogInformation($"Handling external Service task for topic '{lockedTask.TopicName}' with Id '{lockedTask.Id}'.");

        string requestUri = $"{_dispatcherConfig.APIMUrl}/{lockedTask.TopicName.ToLowerInvariant()}?taskId={lockedTask.Id}";
        var client = _clientFactory.CreateClient();

        // execute request
        var content = CreateRequestContent(lockedTask);
        var response = await client.PostAsync(requestUri, content);

        // handle status-code
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvokeErrorException($"Invalid HTTP status-code {response.StatusCode}.");
        }

        // handle response
        return await CreateResponseAsync(response);
    }

    /// <summary>
    /// Create a request for calling an API Management operation for an external task.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <returns>An initialized <see cref="HttpContent"/> instance.</returns>
    private HttpContent CreateRequestContent(LockedExternalTask lockedTask)
    {
        string json = JsonConvert.SerializeObject(lockedTask.Variables,
            typeof(Dictionary<string, VariableValue>), _serializerSettings);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("Ocp-Apim-Subscription-Key", _dispatcherConfig.APIMKey);
        return content;
    }

    /// <summary>
    /// Create a response for completing an external service task.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <returns>A dictionary with the variables returned from the external task implementation.</returns>
    private async Task<Dictionary<string, VariableValue>> CreateResponseAsync(HttpResponseMessage response)
    {
        var responseJson = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonConvert.DeserializeObject(responseJson,
            typeof(APIResponse), _serializerSettings) as APIResponse;

        // log variables
        if (apiResponse != null && apiResponse.Variables != null)
        {
            try
            {
                _logger.LogDebug("Received variables:");
                foreach (var item in apiResponse.Variables)
                {
                    _logger.LogDebug(" - {VariableName}:{VariableValue}", item.Key, item.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error while logging variables:");
                _logger.LogDebug(ex.Message);
            }
            return apiResponse.Variables;
        }
        return new Dictionary<string, VariableValue>();
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

    /// <summary>
    /// Get the API Management subscription key from an Azure KeyVault.
    /// </summary>
    /// <returns>The APIM subscription key of null when the key could not be retrieved from the Azure KeyVault.</returns>
    private async Task<string?> GetAPIMKey()
    {
        int retries = 0;
        while (retries < 5)
        {
            try
            {
                AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
                var keyVaultClient = new KeyVaultClient(
                    new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
                var secret = await keyVaultClient.GetSecretAsync(
                    "https://acikeyvault.vault.azure.net/secrets/APIMKey").ConfigureAwait(false);
                return secret.Value;
            }
            catch (Exception ex)
            {
                var logMessage = new StringBuilder($"Error while retrieving APIMKey from Azure Key Vault:");
                logMessage.AppendLine($"{ex.Message}");
                logMessage.AppendLine("Retrying in 2 sec. ...");
                _logger.LogWarning(logMessage.ToString());
                await Task.Delay(2000);
                retries++;
            }
        }
        _logger.LogError("Unable to retrieve APIMKey from Azure Key Vault.");
        return null;
    }
}
