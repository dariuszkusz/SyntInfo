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
builder.Host.UseWolverine();


// Add services to the container.
// PWA API: Baza Danych PostgreSQL z rozszerzeniem pgvector
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), o => o.UseVector()));


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Rejestracja CQRS i UnitOfWork (Usunięto customowy dyspozytor na rzecz Wolverine)
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Rejestracja klienta LLM
builder.Services.AddHttpClient<SyntInfo.Application.Interfaces.ILlmClient, SyntInfo.Infrastructure.Services.LocalLlmClient>(client =>
{
    var llmUrl = builder.Configuration["Llm:BaseUrl"] ?? "http://localhost:11434/";
    client.BaseAddress = new Uri(llmUrl);
});

// Konfiguracja zadań w tle (Quartz)
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("RssFetcherJob");
    q.AddJob<SyntInfo.Infrastructure.BackgroundJobs.RssFetcherJob>(opts => opts.WithIdentity(jobKey));

    // Odpalanie co 2 minuty (do testów, w prodzie co np. godzinę)
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("RssFetcherJob-trigger")
        .WithSimpleSchedule(x => x.WithIntervalInMinutes(2).RepeatForever()));
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

app.Run();
