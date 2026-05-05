using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using SyntInfo.Infrastructure.Persistence;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Interfaces;
using Wolverine;


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
    });


// Add services to the container.
// PWA API: Baza Danych PostgreSQL z rozszerzeniem pgvector
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector().EnableRetryOnFailure()));


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
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
builder.Services.AddHttpClient();

builder.Services.AddHttpClient<IOpenRouterClient, SyntInfo.Infrastructure.Services.OpenRouterClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1/");
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Konfiguracja zadań w tle (Quartz)
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("RssFetcherJob");
    q.AddJob<SyntInfo.Infrastructure.BackgroundJobs.RssFetcherJob>(opts => opts.WithIdentity(jobKey));

    // Odpalanie 2 razy dziennie (7:00 i 19:00)
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("RssFetcherJob-trigger")
        .WithCronSchedule("0 0 7,19 * * ?")); // O godzinie 7:00 i 19:00 codziennie
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
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
