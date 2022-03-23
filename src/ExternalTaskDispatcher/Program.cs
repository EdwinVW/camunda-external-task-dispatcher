Host
    .CreateDefaultBuilder(args)
    .UseSerilog((hostContext, loggerConfiguration) =>
    {
        loggerConfiguration.ReadFrom.Configuration(hostContext.Configuration);
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHttpClient();

        // register handlers
        services.AddSingleton<IServiceTaskHandler, AzureAPIMTaskHandler>();
        services.AddSingleton<IMessageTaskHandler, AzureAPIMTaskHandler>();

        // register External ServiceTask request/response mappers
        services.AddTransient<ExternalTaskMapperBase>();

        // register External MessageTask request/response mappers
        // TODO
        
        services.AddHostedService<Dispatcher>();
    })
    .Build()
    .Run();
