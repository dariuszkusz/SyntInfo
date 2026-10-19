using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using SyntInfo.Infrastructure.Persistence;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Interfaces;
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
        opts.CodeGeneration.AlwaysUseServiceLocationFor<Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IUnitOfWork>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IOpenRouterClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IGoogleAiStudioClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ISearchService>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ISocialPostGenerator>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ITelegramNotificationService>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<ISocialPublisherFactory>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IWykopClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IHejtoClient>();
        opts.CodeGeneration.AlwaysUseServiceLocationFor<IMailerLiteClient>();
    });


// Add services to the container.
// PWA API: Baza Danych PostgreSQL z rozszerzeniem pgvector
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector().EnableRetryOnFailure()));


builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
// Generowanie specyfikacji OpenAPI dla interfejsu Scalar
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Rejestracja CQRS i UnitOfWork (Usunięto customowy dyspozytor na rzecz Wolverine)
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddHttpClient<ISearchService, SyntInfo.Infrastructure.Services.TavilySearchService>(client => 
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
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

// Rejestracja modułu automatyzacji Social Media & Telegram
builder.Services.AddHttpClient("WykopClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("HejtoClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("FacebookClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("MailerLiteClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IWykopClient, SyntInfo.Infrastructure.Services.WykopClient>();
builder.Services.AddScoped<IHejtoClient, SyntInfo.Infrastructure.Services.HejtoClient>();
builder.Services.AddScoped<IFacebookClient, SyntInfo.Infrastructure.Services.FacebookClient>();
builder.Services.AddScoped<IMailerLiteClient, SyntInfo.Infrastructure.Services.MailerLiteClient>();

builder.Services.AddScoped<ISocialPlatformPublisher, SyntInfo.Infrastructure.Services.Publishers.WykopPublisher>();
builder.Services.AddScoped<ISocialPlatformPublisher, SyntInfo.Infrastructure.Services.Publishers.HejtoPublisher>();
builder.Services.AddScoped<ISocialPlatformPublisher, SyntInfo.Infrastructure.Services.Publishers.FacebookPublisher>();
builder.Services.AddScoped<ISocialPlatformPublisher, SyntInfo.Infrastructure.Services.Publishers.NewsletterPublisher>();
builder.Services.AddScoped<ISocialPostGenerator, SyntInfo.Infrastructure.Services.SocialPostGenerator>();
builder.Services.AddScoped<ITelegramNotificationService, SyntInfo.Infrastructure.Services.TelegramNotificationService>();
builder.Services.AddScoped<ISocialPublisherFactory, SyntInfo.Infrastructure.Services.SocialPublisherFactory>();

// Konfiguracja zadań w tle (Quartz)
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("RssFetcherJob");
    q.AddJob<SyntInfo.Infrastructure.BackgroundJobs.RssFetcherJob>(opts => opts.WithIdentity(jobKey));

    TimeZoneInfo warsawTimeZone;
    try
    {
        warsawTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    }
    catch (TimeZoneNotFoundException)
    {
        warsawTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
    }

    // Odpalanie 2 razy dziennie (7:00 i 19:00 czasu polskiego)
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("RssFetcherJob-trigger")
        .WithCronSchedule("0 0 7,19 * * ?", x => x.InTimeZone(warsawTimeZone)));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

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
