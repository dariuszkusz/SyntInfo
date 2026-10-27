using Microsoft.EntityFrameworkCore;
using Serilog;
using SyntInfo.Infrastructure.Persistence;
using Wolverine;
using Scalar.AspNetCore;


try
{
    var builder = WebApplication.CreateBuilder(args);

    // Konfiguracja Serilog z appsettings.json
    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();

    Log.Information("!!! SYNTINFO STARTING !!!");
    builder.Host.UseSerilog();

    builder.Host.UseWolverine(opts =>
    {
        opts.Discovery.IncludeAssembly(typeof(SyntInfo.Application.CQRS.Handlers.GetNewsArticlesQueryHandler).Assembly);

        // Optymalizacja generowania kodu dla środowiska produkcyjnego
        opts.CodeGeneration.TypeLoadMode = builder.Environment.IsDevelopment()
            ? JasperFx.CodeGeneration.TypeLoadMode.Dynamic
            : JasperFx.CodeGeneration.TypeLoadMode.Auto;

        // Wyciszenie ostrzeżeń o Service Location dla typów rejestrowanych za pomocą fabryk lambda
        opts.CodeGeneration.AlwaysUseServiceLocationFor<AppDbContext>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<DbContextOptions<AppDbContext>>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IUnitOfWork>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IRssFeedClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IWebMetadataExtractor>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IOpenRouterClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IGoogleAiStudioClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ISearchService>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ISocialPostGenerator>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ITelegramNotificationService>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ITelegramCurationStateService>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ISocialPublisherFactory>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IWykopClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IHejtoClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IMailerLiteClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IOptions<ProcessingOptions>>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IOptions<CurationOptions>>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IOptions<MailerLiteOptions>>();
    });


    // Add services to the container.
    builder.Services.AddConfiguredOptions<IConfigurableOptions>(builder.Configuration);

    // PWA API: Baza Danych PostgreSQL z rozszerzeniem pgvector
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            o => o.UseVector().EnableRetryOnFailure()));


    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    // Konfiguracja Swagger / OpenAPI, Uwierzytelniania, CORS i Telegram Bot Client za pomocą metod rozszerzających
    builder.Services.AddSwaggerDocumentation();
    builder.Services.AddAdminAuthentication();
    var appOptions = builder.Configuration.Get<AppOptions>() ?? new AppOptions();
    builder.Services.AddCorsPolicy(appOptions);
    builder.Services.AddTelegramBotClient();


    builder.Services.AddHttpClient<ISearchService, SyntInfo.Infrastructure.Services.TavilySearchService>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(10);
    });
    builder.Services.AddHttpClient<INewsDiscoveryService, SyntInfo.Infrastructure.Services.TavilyNewsDiscoveryService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddHttpClient<IRssFeedClient, SyntInfo.Infrastructure.Services.RssFeedClient>();
    builder.Services.AddHttpClient<IWebMetadataExtractor, SyntInfo.Infrastructure.Services.WebMetadataExtractor>();
    builder.Services.AddHttpClient(string.Empty)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true
        });

    builder.Services.AddHttpClient<IOpenRouterClient, SyntInfo.Infrastructure.Services.OpenRouterClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1/");
        client.Timeout = TimeSpan.FromMinutes(10);
    });

    builder.Services.AddHttpClient<IGoogleAiStudioClient, SyntInfo.Infrastructure.Services.GoogleAiStudioClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(10);
    });

    // Pamięć podręczna (dla tokenów OAuth2 Hejto itp.)
    builder.Services.AddMemoryCache();

    // Rejestracja modułu automatyzacji Social Media & Telegram (klienci HTTP na podstawie IHttpClientOptions)
    builder.Services.AddConfiguredHttpClients<IConfigurableOptions>();

    builder.Services.AddSingleton<ITelegramCurationStateService, SyntInfo.Infrastructure.Services.TelegramCurationStateService>();
    builder.Services.AddInfrastructureServices();

    // Konfiguracja zadań w tle (Quartz)
    builder.Services.AddQuartzBackgroundJobs();

    var app = builder.Build();

    // Konfiguracja specyfikacji OpenAPI i interfejsu Scalar UI (bez duplikatu Swagger UI)
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger(options =>
        {
            options.RouteTemplate = "openapi/{documentName}.json";
        });

        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("SyntInfo API Reference")
                .WithTheme(ScalarTheme.Moon);
        });
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }
    app.UseCors("AllowAngular");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Log.Information("Applying database migrations...");
        db.Database.Migrate();
    }

    // Seed database
    await DataSeeder.SeedAsync(app.Services);

    Log.Information("Backend SyntInfo gotowy do pracy.");
    Log.Information("Dokumentacja API (Scalar): http://localhost:5000/scalar/v1");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Aplikacja zakończyła się niepowodzeniem");
}
finally
{
    Log.CloseAndFlush();
}
