# Camunda External Task dispatcher

.NET implementation of a handler for External Tasks in a Camunda workflow.

## Hosting

This External Task Dispatcher is supposed to be used together with a centrally hosted Camunda engine that is reused for multiple business processes and multiple services. All communication with Camunda is done through its extensive [RESTful Web API](https://docs.camunda.org/manual/latest/reference/rest/):

![Hosting model](img/camunda-hosting.png)

The external services and systems will not communicate directly with the Camunda API. The operations of the Camunda API that need to be available will be abstracted by an API Gateway (AZ-IAG or OP-IAG).  

## External Task mechanism

![Communication patterns](img/etd-communication-patterns.png)

![ETD Behavior](img/etd-behavior.gif)
