namespace ExternalTaskDispatcher.Handlers;

/// <summary>
/// Handles External Tasks triggered by a Message-task in Camunda.
/// </summary>
public interface IMessageTaskHandler
{
    Task<Dictionary<string, VariableValue>> HandleMessageTaskAsync(LockedExternalTask lockedTask);
}