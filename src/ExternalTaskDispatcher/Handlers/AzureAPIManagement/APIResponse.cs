namespace ExternalTaskDispatcher.Handlers.AzureAPIManagement;

/// <summary>
/// Represents a response from an API call.
/// </summary>
public class APIResponse
{
    /// <summary>
    /// The unique Id of the external task.
    /// </summary>
    public string? TaskId { get; set; }

    /// <summary>
    /// The variables returned from the API call.
    /// </summary>
    public Dictionary<string, VariableValue>? Variables { get; set; }
}
