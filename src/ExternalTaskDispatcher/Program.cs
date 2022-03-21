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
        services.AddTransient<svc_BepaalLopendKlantRisicoDossierMapper>();
        services.AddTransient<svc_MaakKlantRisicoDossierAanMapper>();
        services.AddTransient<svc_BepaalLopendContractMapper>();
        services.AddTransient<svc_SluitKlantRisicoDossierMapper>();
        services.AddTransient<svc_GetCustomerInfoMapper>();

        // register External MessageTask request/response mappers
        // TODO
        
        services.AddHostedService<Dispatcher>();
    })
    .Build()
    .Run();
