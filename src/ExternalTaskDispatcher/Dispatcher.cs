using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Camunda.Api.Client;
using Camunda.Api.Client.ExternalTask;
using ExternalTaskDispatcher.Models;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ExternalTaskDispatcher
{
    public class Dispatcher : BackgroundService
    {
        private readonly ILogger<Dispatcher> _logger;
        private readonly IHttpClientFactory _clientFactory;
        private string[] _topics;
        private bool _automaticTopicDiscovery;
        private long _longPollingIntervalInMs;
        private long _taskLockDurationInMs;
        private string _workerId;
        private string _camundaUrl;
        private string _apimUrl;
        private string _apimKey;
        private CamundaClient _camundaClient;
        private DateTime? _lastErrorTimeStamp;
        private JsonSerializerSettings _serializerSettings;

        public Dispatcher(ILogger<Dispatcher> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _clientFactory = httpClientFactory;
            ReadConfig(config);
            _camundaClient = CamundaClient.Create(_camundaUrl);
            _serializerSettings = new JsonSerializerSettings
            {
            };
        }

        private void ReadConfig(IConfiguration config)
        {
            var dispatcherConfig = new DispatcherConfig();
            config.GetSection("Dispatcher").Bind(dispatcherConfig);

            _workerId = $"ExternalTaskDispatcher" + dispatcherConfig.Id;
            _camundaUrl = dispatcherConfig.CamundaUrl;
            _apimUrl = dispatcherConfig.APIMUrl;
            //_apimKey = GetAPIMKey().Result ?? dispatcherConfig.APIMKey;
            _apimKey = dispatcherConfig.APIMKey;
            _topics = dispatcherConfig.Topics.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).ToArray();
            _automaticTopicDiscovery = dispatcherConfig.AutomaticTopicDiscovery;
            _longPollingIntervalInMs = dispatcherConfig.LongPollingIntervalInMs;
            _taskLockDurationInMs = dispatcherConfig.TaskLockDurationInMs;

            var logBuilder = new StringBuilder();
            logBuilder.AppendLine($"Configuration:");
            logBuilder.AppendLine(new string('-',35));
            logBuilder.AppendLine($"Worker Id: {_workerId}");
            logBuilder.AppendLine($"Camunda Url: {_camundaUrl}");
            logBuilder.AppendLine($"APIM Url: {_apimUrl}");
            logBuilder.AppendLine($"APIM Key: {new string('*', _apimKey.Length)}");
            logBuilder.AppendLine($"Pre-configured Topics:");
            foreach(string topic in _topics)
            {
                logBuilder.AppendLine($"- {topic}");
            }
            logBuilder.AppendLine($"Automatic topic discovery: {_automaticTopicDiscovery}");
            logBuilder.AppendLine($"Long polling interval in ms: {_longPollingIntervalInMs}");
            logBuilder.AppendLine($"Task lock duration in ms: {_taskLockDurationInMs}");
            _logger.LogInformation(logBuilder.ToString());
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            FetchExternalTasks fetchInfo = new FetchExternalTasks
            {
                MaxTasks = 1,
                WorkerId = _workerId,
                AsyncResponseTimeout = _longPollingIntervalInMs,
                Topics = _topics.Select(t => new FetchExternalTaskTopic(t, _taskLockDurationInMs)).ToList()
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // discover new topics
                    if (_automaticTopicDiscovery)
                    {
                        _logger.LogInformation($"Start automatic topic discovery");
                        var tasks = await _camundaClient.ExternalTasks.Query().List();
                        if (tasks.Count > 0)
                        {
                            foreach (var task in tasks)
                            {
                                if (!_topics.Contains(task.TopicName))
                                {
                                    _logger.LogInformation($"Discovered new topic {task.TopicName}. Adding it to the list of topics.");
                                    fetchInfo.Topics.Add(new FetchExternalTaskTopic(task.TopicName, _taskLockDurationInMs));
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"No new topics found");
                        }
                    }

                    // skip fetch & lock when no topics are known
                    if (fetchInfo.Topics.Count == 0)
                    {
                        _logger.LogInformation($"No topics known to fetch & lock. Delaying for 10 seconds...");
                        await Task.Delay(10000);
                        continue;
                    }

                    _logger.LogInformation($"Start fetch & lock");

                    var lockedTasks = await _camundaClient.ExternalTasks.FetchAndLock(fetchInfo);
                    _logger.LogInformation($"Found {lockedTasks.Count} tasks");
                    foreach (var lockedTask in lockedTasks)
                    {
                        _logger.LogInformation($"Handling {lockedTask.TopicName} task with Id: {lockedTask.Id}");

                        try
                        {
                            var outputVariables = await HandleTaskAsync(lockedTask);
                            var complete = new CompleteExternalTask
                            {
                                WorkerId = this._workerId,
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

        private async Task HandleFailureAsync(LockedExternalTask lockedTask, Exception ex)
        {
            _logger.LogInformation($"HandleFaiure for {lockedTask.TopicName} task with Id: {lockedTask.Id}");

            try
            {
                int retries = lockedTask.Retries ?? 5;
                var failure = new ExternalTaskFailure
                {
                    WorkerId = this._workerId,
                    ErrorMessage = ex.Message,
                    ErrorDetails = ex.ToString(),
                    Retries = retries - 1,
                    RetryTimeout = 5000
                };
                await _camundaClient.ExternalTasks[lockedTask.Id].HandleFailure(failure);

            }
            catch (Exception handleEx)
            {
                _logger.LogError(handleEx, $"Error while handling faiure for {lockedTask.TopicName} task with Id: {lockedTask.Id}");
            }
        }

        private async Task<Dictionary<string, VariableValue>> HandleTaskAsync(LockedExternalTask lockedTask)
        {
            _logger.LogInformation($"Handle task {lockedTask.TopicName}");

            string requestUri = $"{_apimUrl}/{lockedTask.TopicName.ToLowerInvariant()}?taskId={lockedTask.Id}";
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

        private HttpContent CreateRequestContent(LockedExternalTask lockedTask)
        {
            string json = JsonConvert.SerializeObject(lockedTask.Variables, 
                typeof(Dictionary<string, VariableValue>), _serializerSettings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.Add("Ocp-Apim-Subscription-Key", _apimKey);
            return content;
        }

        private async Task<Dictionary<string, VariableValue>> CreateResponseAsync(HttpResponseMessage response)
        {
            var responseJson = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject(responseJson, 
                typeof(APIResponse), _serializerSettings) as APIResponse;

            // log variables
            try
            {
                _logger.LogDebug("Received variables:");
                foreach(var item in apiResponse.Variables)
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

        private async Task<string> GetAPIMKey()
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
}
