namespace ExternalTaskDispatcher.Handlers;

public interface IMessageTaskHandler
{
    Task<Dictionary<string, VariableValue>> HandleMessageTaskAsync(LockedExternalTask lockedTask);
}