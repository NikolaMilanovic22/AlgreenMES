using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AlGreenMES.Modules.Identity.Infrastructure.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user)
        => BuildToken(user, effectiveTenantId: user.TenantId, isCrossTenantSession: false);

    public string GenerateCrossTenantToken(User superAdminUser, Guid targetTenantId)
        => BuildToken(superAdminUser, effectiveTenantId: targetTenantId, isCrossTenantSession: true);

    private string BuildToken(User user, Guid effectiveTenantId, bool isCrossTenantSession)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"]!;
        var issuer = jwtSettings["Issuer"]!;
        var audience = jwtSettings["Audience"]!;
        var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"]!);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("tenant_id", effectiveTenantId.ToString()),
            new("first_name", user.FirstName),
            new("last_name", user.LastName)
        };

        if (isCrossTenantSession)
        {
            // Read-only middleware reads this claim to gate writes; FE reads
            // it to render the cross-tenant warning banner. Also stash the
            // user's home tenant so FE can show "Vrati se na svoj nalog".
            claims.Add(new Claim("cross_tenant_session", "true"));
            claims.Add(new Claim("home_tenant_id", user.TenantId.ToString()));
        }

        // One Role claim per effective role (primary + additional). Saša
        // 08.06.2026 — a user can be e.g. Coordinator + Magacioner; both
        // claims emitted so [Authorize(Roles = "Magacioner")] picks them up
        // without further changes.
        foreach (var role in user.EffectiveRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
