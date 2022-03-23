namespace ExternalTaskDispatcher.Handlers;

/// <summary>
/// Handles External Tasks triggered by a Service-task in Camunda.
/// </summary>
public interface IServiceTaskHandler
{
    Task<Dictionary<string, VariableValue>> HandleServiceTaskAsync(LockedExternalTask lockedTask);
}