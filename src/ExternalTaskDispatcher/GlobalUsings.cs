global using System;
global using System.Net;
global using System.Text;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Net.Http;
global using System.Collections.Generic;
global using System.Text.RegularExpressions;
global using Camunda.Api.Client;
global using Camunda.Api.Client.ExternalTask;
global using Microsoft.Azure.KeyVault;
global using Microsoft.Azure.Services.AppAuthentication;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Newtonsoft.Json;
global using System.Runtime.Serialization;
global using Serilog;
global using ExternalTaskDispatcher;
global using ExternalTaskDispatcher.Models;
global using ExternalTaskDispatcher.Handlers;
global using ExternalTaskDispatcher.Mappers;
global using ExternalTaskDispatcher.Handlers.AzureAPIManagement;
