using AutomationFunctions.Options;
using AutomationFunctions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(config =>
    {
        // Local-dev secrets live outside the repo (~/.microsoft/usersecrets/...), so the
        // Gmail/IMAP passwords never sit in a project file. Set them with, e.g.:
        //   dotnet user-secrets set "Email:Password" "your-app-password"
        // This is a no-op in Azure — there, use the Function App's Application settings.
        config.AddUserSecrets(typeof(Program).Assembly, optional: true);
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // Strongly-typed configuration. In local.settings.json / app settings,
        // nested keys use the double-underscore convention, e.g. "Email__SmtpHost".
        services.AddOptions<LlmOptions>().Bind(config.GetSection(LlmOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.SectionName));
        services.AddOptions<WeatherOptions>().Bind(config.GetSection(WeatherOptions.SectionName));
        services.AddOptions<SummaryOptions>().Bind(config.GetSection(SummaryOptions.SectionName));
        services.AddOptions<MailScanOptions>().Bind(config.GetSection(MailScanOptions.SectionName));
        services.AddOptions<AviationOptions>().Bind(config.GetSection(AviationOptions.SectionName));

        services.AddHttpClient();

        // Automation building blocks.
        services.AddSingleton<IWebPageFetcher, WebPageFetcher>();
        services.AddSingleton<ILlmService, OpenAiCompatibleLlmService>();
        services.AddSingleton<IEmailService, SmtpEmailService>();
        services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
        services.AddSingleton<IAviationWeatherService, AviationWeatherService>();
        services.AddSingleton<IMailScanner, ImapMailScanner>();

        // Orchestrator that composes the building blocks into one report.
        services.AddSingleton<IDigestService, DigestService>();
    })
    .Build();

host.Run();
