using RiftboundTcg.Server.Api;
using RiftboundTcg.Server.Api.Data;
using RiftboundTcg.Server.Api.Realtime;
using RiftboundTcg.Server.Api.Services;
using Microsoft.EntityFrameworkCore;
using riftbound_tcg.Engine.RulesEngine;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(_ => builder.Environment.IsDevelopment())
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("riftbound")));
builder.Services.AddSignalR();
builder.Services.AddScoped<OnlineGameService>();
builder.Services.AddSingleton<IRulesEngine, DefaultRulesEngine>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGameApiV1();
app.MapHub<MatchHub>("/hubs/matches");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
