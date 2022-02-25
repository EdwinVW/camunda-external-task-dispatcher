namespace ExternalTaskDispatcher.Handlers.AzureAPIManagement;

/// <summary>
/// Respresents the settings for the AzureAPIMTaskHandler.
/// </summary>
public class AzureAPIMTaskHandlerConfig
{
    public string APIMUrl { get; set; }
    public string APIMKey { get; set; }
    public string APIMKeySecretUrl { get; set; }

    /// <summary>
    /// Constructor that initializes all the defaults.
    /// </summary>
    private AzureAPIMTaskHandlerConfig()
    {
        APIMUrl = string.Empty;
        APIMKey = string.Empty;
        APIMKeySecretUrl = string.Empty;
    }

    /// <summary>
    /// Create a filled AzureAPIMTaskHandlerConfig instance.
    /// </summary>
    /// <param name="configuration">The .NET configuration.</param>
    /// <returns>An initialized <see cref="AzureAPIMTaskHandlerConfig"/> instance.</returns>
    public static AzureAPIMTaskHandlerConfig Build(IConfiguration configuration)
    {
        var config = new AzureAPIMTaskHandlerConfig();
        configuration.GetSection("AzureAPIMTaskHandler").Bind(config);
        return config;
    }

    /// <summary>
    /// Log configuration.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    public void Log(Microsoft.Extensions.Logging.ILogger logger)
    {
        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"AzureAPIMTaskHandler Configuration:");
        logBuilder.AppendLine(new string('-', 60));
        logBuilder.AppendLine($"APIM Url            : {APIMUrl}");
        logBuilder.AppendLine($"APIM Key Secret Url : {APIMKeySecretUrl}");
        logBuilder.AppendLine($"APIM Key            : {new string('*', APIMKey.Length)}");
        logBuilder.AppendLine(new string('-', 60));

        logger.LogInformation(logBuilder.ToString());
    }
}
