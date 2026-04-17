using Microsoft.EntityFrameworkCore;
using Quartz;
using SyntInfo.Infrastructure.Persistence;
using SyntInfo.Application.Interfaces;
using SyntInfo.Domain.Interfaces;
using SyntInfo.Application.CQRS.Queries;
using SyntInfo.Application.CQRS.Handlers;
using System.Collections.Generic;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
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

// Rejestracja CQRS i UnitOfWork (Usunięto customowy dyspozytor na rzecz Wolverine)
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddHttpClient();

// Rejestracja klienta LLM
builder.Services.AddHttpClient<SyntInfo.Application.Interfaces.ILlmClient, SyntInfo.Infrastructure.Services.LocalLlmClient>(client =>
{
    var llmUrl = builder.Configuration["Llm:BaseUrl"] ?? "http://localhost:11434/";
    client.BaseAddress = new Uri(llmUrl);
    client.Timeout = TimeSpan.FromMinutes(5); // Zwiększony timeout dla lokalnego modelu LLM
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
app.UseAuthorization();
app.MapControllers();

// Seed database
await SyntInfo.Infrastructure.Persistence.DataSeeder.SeedAsync(app.Services);

app.Run();
