using RiftboundTcg.Server.Api;
using RiftboundTcg.Server.Api.Realtime;
using RiftboundTcg.Server.Api.Services;
using riftbound_tcg.Engine.RulesEngine;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PlaceholderGameStore>();
builder.Services.AddSingleton<IRulesEngine, DefaultRulesEngine>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGameApiV1();
app.MapHub<MatchHub>("/hubs/matches");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
