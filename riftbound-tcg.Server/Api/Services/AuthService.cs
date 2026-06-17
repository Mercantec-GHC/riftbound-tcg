using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RiftboundTcg.Server.Api.Data;
using RiftboundTcg.Server.Api.Models;

namespace RiftboundTcg.Server.Api.Services;

public sealed class AuthSettings
{
    public string Issuer { get; set; } = "riftbound-tcg";
    public string Audience { get; set; } = "riftbound-tcg";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 14;
}

public sealed class AdminSettings
{
    public string Email { get; set; } = "admin@riftbound.local";
    public string Password { get; set; } = "ChangeMe123!";
    public string DisplayName { get; set; } = "Admin";
}

public sealed class AuthService(
    GameDbContext db,
    PasswordHasher<UserEntity> passwordHasher,
    IOptions<AuthSettings> options,
    IOptions<AdminSettings> adminOptions)
{
    private readonly AuthSettings _settings = options.Value;
    private readonly AdminSettings _adminSettings = adminOptions.Value;

    public async Task EnsureAuthReadyAsync(CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
    }

    public async Task<AuthSessionDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email is required.");
        }

        if (request.Password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }

        if (await db.Users.AnyAsync(user => user.NormalizedEmail == email, cancellationToken))
        {
            throw new InvalidOperationException("Email is already registered.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new UserEntity
        {
            Id = $"user-{Guid.NewGuid():N}",
            Email = request.Email.Trim(),
            NormalizedEmail = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email.Trim() : request.DisplayName.Trim(),
            IsAdmin = false,
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return await CreateSessionAsync(user, cancellationToken);
    }

    public async Task<AuthSessionDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.NormalizedEmail == email, cancellationToken)
            ?? throw new InvalidOperationException("Invalid email or password.");

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException("Invalid email or password.");
        }

        if (user.IsDisabled)
        {
            throw new InvalidOperationException("Account is disabled.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = user.LastLoginAt.Value;
        await db.SaveChangesAsync(cancellationToken);
        return await CreateSessionAsync(user, cancellationToken);
    }

    public async Task<AuthSessionDto> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var hash = HashToken(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken)
            ?? throw new InvalidOperationException("Refresh token is invalid.");
        if (token.RevokedAt is not null || token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Refresh token is expired or revoked.");
        }

        var user = await db.Users.FindAsync([token.UserId], cancellationToken)
            ?? throw new InvalidOperationException("Refresh token user was not found.");
        if (user.IsDisabled)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Account is disabled.");
        }

        token.RevokedAt = DateTimeOffset.UtcNow;
        return await CreateSessionAsync(user, cancellationToken);
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var hash = HashToken(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (token is not null && token.RevokedAt is null)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<UserDto?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        return user is null ? null : ToDto(user);
    }

    public async Task<UserDto?> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName.Trim();
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public static UserDto ToDto(UserEntity user)
    {
        return new UserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.CreatedAt,
            new UserStatsDto(user.GamesPlayed, user.Wins, user.Losses, user.PointsScored, user.LastPlayedAt),
            user.IsAdmin,
            user.IsDisabled,
            user.LastLoginAt,
            user.DisabledAt);
    }

    public static string? GetUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    }

    private async Task<AuthSessionDto> CreateSessionAsync(UserEntity user, CancellationToken cancellationToken)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(5, _settings.AccessTokenMinutes));
        var accessToken = CreateAccessToken(user, expiresAt);
        var refreshToken = CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = $"refresh-{Guid.NewGuid():N}",
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _settings.RefreshTokenDays))
        });
        await db.SaveChangesAsync(cancellationToken);
        return new AuthSessionDto(accessToken, refreshToken, expiresAt, ToDto(user));
    }

    private async Task EnsureAuthSchemaAsync(CancellationToken cancellationToken)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Email" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "NormalizedEmail" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PasswordHash" text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsAdmin" boolean NOT NULL DEFAULT false;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsDisabled" boolean NOT NULL DEFAULT false;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT now();
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "DisabledAt" timestamptz NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "LastLoginAt" timestamptz NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "GamesPlayed" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Wins" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "Losses" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PointsScored" integer NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "LastPlayedAt" timestamptz NULL;
            UPDATE users SET "Email" = lower("Id") || '@legacy.riftbound.local' WHERE "Email" = '';
            UPDATE users SET "NormalizedEmail" = upper("Email") WHERE "NormalizedEmail" = '';

            CREATE TABLE IF NOT EXISTS refresh_tokens (
                "Id" text PRIMARY KEY,
                "UserId" text NOT NULL,
                "TokenHash" text NOT NULL,
                "ExpiresAt" timestamptz NOT NULL,
                "RevokedAt" timestamptz NULL,
                "CreatedAt" timestamptz NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_NormalizedEmail" ON users("NormalizedEmail");
            CREATE INDEX IF NOT EXISTS "IX_refresh_tokens_UserId" ON refresh_tokens("UserId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_refresh_tokens_TokenHash" ON refresh_tokens("TokenHash");
            """, cancellationToken);
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var email = string.IsNullOrWhiteSpace(_adminSettings.Email) ? "admin@riftbound.local" : _adminSettings.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);
        var password = string.IsNullOrWhiteSpace(_adminSettings.Password) ? "ChangeMe123!" : _adminSettings.Password;
        var now = DateTimeOffset.UtcNow;
        var admin = await db.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
        if (admin is null)
        {
            admin = new UserEntity
            {
                Id = "user-admin",
                Email = email,
                NormalizedEmail = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(_adminSettings.DisplayName) ? "Admin" : _adminSettings.DisplayName.Trim(),
                IsAdmin = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            admin.PasswordHash = passwordHasher.HashPassword(admin, password);
            db.Users.Add(admin);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var changed = false;
        if (!admin.IsAdmin)
        {
            admin.IsAdmin = true;
            changed = true;
        }

        if (admin.IsDisabled)
        {
            admin.IsDisabled = false;
            admin.DisabledAt = null;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(admin.PasswordHash))
        {
            admin.PasswordHash = passwordHasher.HashPassword(admin, password);
            changed = true;
        }

        if (changed)
        {
            admin.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private string CreateAccessToken(UserEntity user, DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("admin", user.IsAdmin ? "true" : "false"),
            new Claim(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User")
        };

        var token = new JwtSecurityToken(
            _settings.Issuer,
            _settings.Audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }
}
