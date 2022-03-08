namespace ExternalTaskDispatcher.Handlers.AzureAPIManagement;

public class AzureAPIMTaskHandler : IServiceTaskHandler, IMessageTaskHandler
{
    private AzureAPIMTaskHandlerConfig _config;
    private readonly ILogger<AzureAPIMTaskHandler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _clientFactory;
    private JsonSerializerSettings _serializerSettings;

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="configuration">The .NET configuration.</param>
    /// <param name="httpClientFactory">The factory for safelay creating HTTPClient instances.</param>
    public AzureAPIMTaskHandler(ILogger<AzureAPIMTaskHandler> logger, IConfiguration configuration, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _clientFactory = httpClientFactory;
        _config = AzureAPIMTaskHandlerConfig.Build(configuration);

        // get APIM Key from Key Vault
        if (!string.IsNullOrEmpty(_config.APIMKeySecretUrl))
        {
            _config.APIMKey = GetAPIMKey().Result ?? _config.APIMKey;
        }

        _config.Log(logger);
        _serializerSettings = new JsonSerializerSettings
        {
        };
    }

    /// <summary>
    /// Handle an external task of type ServiceTask.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <returns>A dictionary with the variables returned from the external task implementation.</returns>
    public async Task<Dictionary<string, VariableValue>> HandleServiceTaskAsync(LockedExternalTask lockedTask)
    {
        _logger.LogInformation($"Handling external Service task for topic '{lockedTask.TopicName}' with Id '{lockedTask.Id}'.");

        string requestUri = $"{_config.APIMUrl}/{lockedTask.TopicName.ToLowerInvariant()}?taskId={lockedTask.Id}";
        var client = _clientFactory.CreateClient();

        _logger.LogInformation($"Request Uri: '{requestUri}'");

        // execute request
        var mapper = Getmapper(lockedTask);
        var json = mapper.CreateRequestJson(lockedTask);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("Ocp-Apim-Subscription-Key", _config.APIMKey);
        var response = await client.PostAsync(requestUri, content);

        // handle status-code
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvokeErrorException($"Invalid HTTP status-code {response.StatusCode}.");
        }

        // handle response
        var camundaResponse = await mapper.CreateCamundaResponseAsync(response);
        LogVariables(camundaResponse);

        return camundaResponse!;
    }

    /// <summary>
    /// Handle an external task of type Message.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    /// <returns>A dictionary with the variables returned from the external task implementation.</returns>
    public Task<Dictionary<string, VariableValue>> HandleMessageTaskAsync(LockedExternalTask lockedTask)
    {
        // TODO: add message implementation (send to servicebus / kafka)

        _logger.LogInformation($"External task with Id '{lockedTask.Id}' has type 'Message'. Ignoring task.");
        return Task.FromResult(new Dictionary<string, VariableValue>());
    }

    /// <summary>
    /// Get the request-/response-mapper for the specified external task.
    /// </summary>
    /// <param name="lockedTask">The external task to get the mapper for.</param>
    private IExternalTaskMapper Getmapper(LockedExternalTask lockedTask)
    {
        var mapperTypeName = $"ExternalTaskDispatcher.Mappers.{lockedTask.TopicName}Mapper";

        _logger.LogDebug($"Resolve external-task mapper type '{mapperTypeName}'.");

        Type? mapperType = Type.GetType(mapperTypeName);
        if (mapperType == null)
        {
            throw new NotSupportedException($"External-task mapper type '{mapperTypeName}' not found in assembly.");
        }
        var mapper = _serviceProvider.GetRequiredService(mapperType);
        if (mapper == null)
        {
            throw new NotSupportedException($"External-task mapper '{mapperTypeName}' could be resolved.");
        }
        return (IExternalTaskMapper)mapper;
    }

    /// <summary>
    /// Log the variables in a Camunda response.
    /// </summary>
    /// <param name="camundaResponse">The response to log.</param>
    private void LogVariables(Dictionary<string, VariableValue> camundaResponse)
    {
        if (camundaResponse != null && camundaResponse.Any())
        {
            try
            {
                _logger.LogDebug("Received variables:");
                foreach (var item in camundaResponse)
                {
                    _logger.LogDebug($" - {item.Key}:{item.Value}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error while logging variables:");
                _logger.LogDebug(ex.Message);
            }
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
                var secret = await keyVaultClient.GetSecretAsync(_config.APIMKeySecretUrl).ConfigureAwait(false);
                return secret.Value;
            }
            catch (Exception ex)
            {
                var logMessage = new StringBuilder($"Error while retrieving APIMKey from Azure Key Vault:");
                logMessage.AppendLine($"{ex.Message}");
                logMessage.AppendLine("Retrying...");
                _logger.LogWarning(logMessage.ToString());
                await Task.Delay(500);
                retries++;
            }
        }
        _logger.LogError("Unable to retrieve APIMKey from Azure Key Vault.");
        return null;
    }
}
