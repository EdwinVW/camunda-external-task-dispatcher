namespace ExternalTaskDispatcher.Mappers;

public interface IExternalTaskMapper
{
    /// <summary>
    /// Create a JSON string that contains the request body for calling an API Management operation.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    string CreateRequestJson(LockedExternalTask lockedTask);

    /// <summary>
    /// Create a response for completing an external service task.
    /// </summary>
    /// <param name="response">The response receive from the external system.</param>
    /// <returns>A dictionary with the variables returned from the external task implementation.</returns>
    Task<Dictionary<string, VariableValue>> CreateCamundaResponseAsync(HttpResponseMessage response);
}