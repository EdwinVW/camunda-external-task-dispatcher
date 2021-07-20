using System.Collections.Generic;
using Camunda.Api.Client;

namespace ExternalTaskDispatcher.Models
{
    public class APIResponse
    {
        public string TaskId { get; set; }
        public Dictionary<string, VariableValue> Variables { get; set; }
    }
}