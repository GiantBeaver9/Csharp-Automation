using AutomationFunctions.Options;
using AutomationFunctions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // Strongly-typed configuration. In local.settings.json / app settings,
        // nested keys use the double-underscore convention, e.g. "Email__SmtpHost".
        services.AddOptions<LlmOptions>().Bind(config.GetSection(LlmOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.SectionName));
        services.AddOptions<WeatherOptions>().Bind(config.GetSection(WeatherOptions.SectionName));
        services.AddOptions<SummaryOptions>().Bind(config.GetSection(SummaryOptions.SectionName));

        services.AddHttpClient();

        // Automation building blocks. Each timer/HTTP function composes these.
        services.AddSingleton<IWebPageFetcher, WebPageFetcher>();
        services.AddSingleton<ILlmService, OpenAiCompatibleLlmService>();
        services.AddSingleton<IEmailService, SmtpEmailService>();
        services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
    })
    .Build();

host.Run();
