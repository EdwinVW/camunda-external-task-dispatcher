namespace ExternalTaskDispatcher;

/// <summary>
/// Exception signalling an error while invoking an external API.
/// </summary>
public class InvokeErrorException : Exception
{
    public InvokeErrorException()
    {
    }

    public InvokeErrorException(string message) : base(message)
    {
    }

    public InvokeErrorException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected InvokeErrorException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
