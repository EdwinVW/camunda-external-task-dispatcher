namespace ExternalTaskDispatcher.Mappers;

public class ExternalTaskMapperBase
{
    protected JsonSerializerSettings _serializerSettings;

    public ExternalTaskMapperBase()
    {
        _serializerSettings = new JsonSerializerSettings
        {
        };
    }
}