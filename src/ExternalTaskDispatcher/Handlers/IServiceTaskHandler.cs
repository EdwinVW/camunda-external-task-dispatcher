namespace ExternalTaskDispatcher.Handlers;

public interface IServiceTaskHandler
{
    Task<Dictionary<string, VariableValue>> HandleServiceTaskAsync(LockedExternalTask lockedTask);
}