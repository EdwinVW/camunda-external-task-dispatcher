Host
    .CreateDefaultBuilder(args)
    .UseSerilog((hostContext, loggerConfiguration) =>
    {
        loggerConfiguration.ReadFrom.Configuration(hostContext.Configuration);
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHttpClient();
        services.AddSingleton<IServiceTaskHandler, AzureAPIMTaskHandler>();
        services.AddSingleton<IMessageTaskHandler, AzureAPIMTaskHandler>();
        services.AddHostedService<Dispatcher>();
    })
    .Build()
    .Run();
