namespace ExternalTaskDispatcher.Mappers;

/// <summary>
/// Base implementation for mapping External-task variables to an HTTP request 
/// and an HTTP response back to Camunda variables.
/// </summary>
public class ExternalTaskMapperBase : IExternalTaskMapper
{
    protected JsonSerializerSettings _serializerSettings;

    public ExternalTaskMapperBase()
    {
        _serializerSettings = new JsonSerializerSettings
        {
        };
    }

    /// <summary>
    /// Create a JSON string that contains the request body for calling an API Management operation.
    /// </summary>
    /// <param name="lockedTask">The external task information.</param>
    public virtual string CreateRequestJson(LockedExternalTask lockedTask)
    {
        return JsonConvert.SerializeObject(lockedTask.Variables,
            typeof(Dictionary<string, VariableValue>), _serializerSettings);
    }

    /// <summary>
    /// Create a response for completing an external service task.
    /// </summary>
    /// <param name="response">The response receive from the external system.</param>
    /// <returns>A dictionary with the variables returned from the external task implementation.</returns>    
    public virtual async Task<Dictionary<string, VariableValue>> CreateCamundaResponseAsync(HttpResponseMessage response)
    {
        var responseJson = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonConvert.DeserializeObject(responseJson,
            typeof(APIResponse), _serializerSettings) as APIResponse;

        if (apiResponse != null && apiResponse.Variables != null)
        {
            return apiResponse.Variables;
        }
        return new Dictionary<string, VariableValue>();
    }    
}