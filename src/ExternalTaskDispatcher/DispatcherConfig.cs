namespace ExternalTaskDispatcher.Models;

/// <summary>
/// Respresents the settings for the dispatcher.
/// </summary>
public class DispatcherConfig
{
    private string _workerId;

    public string WorkerId
    {
        get => _workerId;
        set => _workerId = _workerId ?? $"{value}-{CreateUniqueId()}";
    }

    public string CamundaUrl { get; set; }
    public List<string> Topics { get; set; }
    public bool AutomaticTopicDiscovery { get; set; }
    public long LongPollingIntervalInMs { get; set; }
    public long TaskLockDurationInMs { get; set; }
    public long TopicCacheInvalidationIntervalInMin { get; set; }
    public string ServiceTaskPrefix { get; set; }
    public string MessageTaskPrefix { get; set; }

    /// <summary>
    /// Constructor that initializes all the defaults.
    /// </summary>
    private DispatcherConfig()
    {
        _workerId = $"ETD-{CreateUniqueId()}";
        CamundaUrl = "http://camunda:8080/engine-rest";
        Topics = new List<string>();
        ServiceTaskPrefix = "Svc-";
        MessageTaskPrefix = "Msg-";
    }

    /// <summary>
    /// Create a filled DispatcherConfig instance.
    /// </summary>
    /// <param name="configuration">The .NET configuration.</param>
    /// <returns>An initialized <see cref="DispatcherConfig"/> instance.</returns>
    public static DispatcherConfig Build(IConfiguration configuration)
    {
        var config = new DispatcherConfig();
        configuration.GetSection("Dispatcher").Bind(config);
        return config;        
    }    

    /// <summary>
    /// Log configuration.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    public void Log(Microsoft.Extensions.Logging.ILogger logger)
    {
        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"Dispatcher Configuration:");
        logBuilder.AppendLine(new string('-', 60));
        logBuilder.AppendLine($"Worker Id: {WorkerId}");
        logBuilder.AppendLine($"Camunda Url: {CamundaUrl}");
        logBuilder.AppendLine($"Pre-configured Topics:");
        foreach (string topic in Topics)
        {
            logBuilder.AppendLine($"- {topic}");
        }
        logBuilder.AppendLine($"Automatic topic discovery: {AutomaticTopicDiscovery}");
        logBuilder.AppendLine($"Long polling interval in ms: {LongPollingIntervalInMs}");
        logBuilder.AppendLine($"Task lock duration in ms: {TaskLockDurationInMs}");
        logBuilder.AppendLine($"Topic cache invalidation interval in minutes: {TopicCacheInvalidationIntervalInMin}");
        logBuilder.AppendLine(new string('-', 60));

        logger.LogInformation(logBuilder.ToString());
    }

    private string CreateUniqueId()
    {
        return Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
    }
}
