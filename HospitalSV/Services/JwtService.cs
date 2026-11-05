using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HospitalSV.Models;
using Microsoft.IdentityModel.Tokens;

namespace HospitalSV.Services;

public interface IJwtService
{
    /// <summary>Issues a signed JWT. Demo tokens carry an extra "demo" claim.</summary>
    string GenerateToken(AppUser user, bool isDemo = false);

    /// <summary>Expiration instant matching the configured token lifetime.</summary>
    DateTime GetExpirationUtc();
}

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(AppUser user, bool isDemo = false)
    {
        var jwtSection = _config.GetSection("Jwt");
        var secret     = jwtSection["Secret"]   ?? throw new InvalidOperationException("JWT Secret is not configured.");
        var issuer     = jwtSection["Issuer"]   ?? "HospitalSV";
        var audience   = jwtSection["Audience"] ?? "HospitalSV";

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name,  user.FullName),
            new(ClaimTypes.Role,               user.Role),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
        };

        if (isDemo)
            claims.Add(new Claim("demo", "true"));

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            GetExpirationUtc(),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime GetExpirationUtc()
    {
        var expiresIn = int.Parse(_config.GetSection("Jwt")["ExpiresInMinutes"] ?? "60");
        return DateTime.UtcNow.AddMinutes(expiresIn);
    }
}
