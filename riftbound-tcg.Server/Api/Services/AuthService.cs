using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
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
    private const long MaxAvatarBytes = 2 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> AllowedAvatarContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

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

    public async Task ChangePasswordAsync(string userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Authenticated user was not found.");

        if (user.IsDisabled)
        {
            throw new InvalidOperationException("Account is disabled.");
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw new InvalidOperationException("Current password could not be verified.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        if (request.NewPassword.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var preservedTokenHash = string.IsNullOrWhiteSpace(request.CurrentRefreshToken)
            ? null
            : HashToken(request.CurrentRefreshToken);
        var activeTokens = await db.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAt == null && token.TokenHash != preservedTokenHash)
            .ToArrayAsync(cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = user.UpdatedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserDto?> UploadAvatarAsync(string userId, IFormFile image, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (image.Length <= 0)
        {
            throw new InvalidOperationException("Avatar image is required.");
        }

        if (image.Length > MaxAvatarBytes)
        {
            throw new InvalidOperationException("Avatar image must be 2 MB or smaller.");
        }

        var contentType = image.ContentType.Trim().ToLowerInvariant();
        if (!AllowedAvatarContentTypes.ContainsKey(contentType))
        {
            throw new InvalidOperationException("Avatar image must be PNG, JPEG, WebP, or GIF.");
        }

        await using var stream = image.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (!LooksLikeAllowedImage(bytes, contentType))
        {
            throw new InvalidOperationException("Avatar image content does not match its file type.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!await db.ProfileImages.AnyAsync(candidate => candidate.Hash == hash, cancellationToken))
        {
            db.ProfileImages.Add(new ProfileImageEntity
            {
                Hash = hash,
                ContentType = contentType,
                Bytes = bytes,
                Length = bytes.Length,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        user.AvatarImageHash = hash;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<UserDto?> ClearAvatarAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.AvatarImageHash = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<ProfileImageEntity?> GetProfileImageAsync(string hash, CancellationToken cancellationToken)
    {
        await EnsureAuthSchemaAsync(cancellationToken);
        return await db.ProfileImages.FindAsync([hash.ToLowerInvariant()], cancellationToken);
    }

    public static UserDto ToDto(UserEntity user)
    {
        return new UserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.AvatarImageHash,
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
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "AvatarImageHash" text NULL;
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
            CREATE TABLE IF NOT EXISTS profile_images (
                "Hash" text PRIMARY KEY,
                "ContentType" text NOT NULL,
                "Bytes" bytea NOT NULL,
                "Length" integer NOT NULL,
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

    private static bool LooksLikeAllowedImage(byte[] bytes, string contentType)
    {
        return contentType switch
        {
            "image/png" => bytes.Length >= 8
                && bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4e
                && bytes[3] == 0x47
                && bytes[4] == 0x0d
                && bytes[5] == 0x0a
                && bytes[6] == 0x1a
                && bytes[7] == 0x0a,
            "image/jpeg" => bytes.Length >= 3
                && bytes[0] == 0xff
                && bytes[1] == 0xd8
                && bytes[2] == 0xff,
            "image/webp" => bytes.Length >= 12
                && bytes[0] == 0x52
                && bytes[1] == 0x49
                && bytes[2] == 0x46
                && bytes[3] == 0x46
                && bytes[8] == 0x57
                && bytes[9] == 0x45
                && bytes[10] == 0x42
                && bytes[11] == 0x50,
            "image/gif" => bytes.Length >= 6
                && bytes[0] == 0x47
                && bytes[1] == 0x49
                && bytes[2] == 0x46
                && bytes[3] == 0x38
                && (bytes[4] == 0x37 || bytes[4] == 0x39)
                && bytes[5] == 0x61,
            _ => false
        };
    }
}
