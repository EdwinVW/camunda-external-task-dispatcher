namespace ExternalTaskDispatcher.Models;

/// <summary>
/// Respresents the settings for the dispatcher.
/// </summary>
public class DispatcherConfig
{
    public string WorkerId { get; set; }
    public string CamundaUrl { get; set; }
    public string APIMUrl { get; set; }
    public string APIMKey { get; set; }
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
    public DispatcherConfig()
    {
        WorkerId = Guid.NewGuid().ToString("D");
        CamundaUrl = "http://camunda:8080/engine-rest";
        APIMUrl = string.Empty;
        APIMKey = string.Empty;
        Topics = new List<string>();
        ServiceTaskPrefix = "Svc-";
        MessageTaskPrefix = "Msg-";
    }
}
