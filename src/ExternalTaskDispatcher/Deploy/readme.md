# Environment Variables

The docker-compose file in this folder uses a Docker `.env.` file for specifying environment variables to the external task dispatcher container. In this file you can specify the Url of your API Management instance (`AzureAPIMTaskHandler__APIMUrl`) and the corresponding access key for gaining access to the APIs in this APIM instance (`AzureAPIMTaskHandler__APIMKey`).

## Instructions

Rename the `cetd-variables.env.sample` file to `cetd-variables.env` and fill in the correct APIM url and APIM access key for your situation. The `cetd-variables.env` file is ignored and will never be pushed to the Git repo.
