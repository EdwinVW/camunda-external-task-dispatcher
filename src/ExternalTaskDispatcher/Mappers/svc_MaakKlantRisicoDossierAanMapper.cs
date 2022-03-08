namespace ExternalTaskDispatcher.Mappers;

public class svc_MaakKlantRisicoDossierAanMapper : ExternalTaskMapperBase, IExternalTaskMapper
{
    public string CreateRequestJson(LockedExternalTask lockedTask)
    {
        return JsonConvert.SerializeObject(lockedTask.Variables,
            typeof(Dictionary<string, VariableValue>), _serializerSettings);
    }
    
    public async Task<Dictionary<string, VariableValue>> CreateCamundaResponseAsync(HttpResponseMessage response)
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