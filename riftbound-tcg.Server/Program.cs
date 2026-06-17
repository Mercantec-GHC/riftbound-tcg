using RiftboundTcg.Server.Api;
using RiftboundTcg.Server.Api.Data;
using RiftboundTcg.Server.Api.Realtime;
using RiftboundTcg.Server.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using riftbound_tcg.Engine.RulesEngine;
using System.Security.Cryptography;
using System.Text;

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
var authSigningKey = builder.Configuration["Auth:SigningKey"];
if (string.IsNullOrWhiteSpace(authSigningKey))
{
    authSigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

builder.Services.Configure<AuthSettings>(settings =>
{
    settings.Issuer = builder.Configuration["Auth:Issuer"] ?? "riftbound-tcg";
    settings.Audience = builder.Configuration["Auth:Audience"] ?? "riftbound-tcg";
    settings.SigningKey = authSigningKey;
    settings.AccessTokenMinutes = int.TryParse(builder.Configuration["Auth:AccessTokenMinutes"], out var accessMinutes) ? accessMinutes : 60;
    settings.RefreshTokenDays = int.TryParse(builder.Configuration["Auth:RefreshTokenDays"], out var refreshDays) ? refreshDays : 14;
});
builder.Services.Configure<AdminSettings>(settings =>
{
    settings.Email = builder.Configuration["Admin:Email"] ?? "admin@riftbound.local";
    settings.Password = builder.Configuration["Admin:Password"] ?? "ChangeMe123!";
    settings.DisplayName = builder.Configuration["Admin:DisplayName"] ?? "Admin";
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"] ?? "riftbound-tcg",
            ValidAudience = builder.Configuration["Auth:Audience"] ?? "riftbound-tcg",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authSigningKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/matches"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireClaim("admin", "true"));
});
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PasswordHasher<UserEntity>>();
builder.Services.AddScoped<OnlineGameService>();
builder.Services.AddSingleton<IRulesEngine, DefaultRulesEngine>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureAuthReadyAsync(CancellationToken.None);
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGameApiV1();
app.MapHub<MatchHub>("/hubs/matches");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();
