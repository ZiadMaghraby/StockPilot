using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using StockPilot.Web.Domain;

namespace StockPilot.Web.Infrastructure;

public class TenantSession(IHttpContextAccessor accessor)
{
    internal Guid? SeedTenantId { get; set; }
    public Guid Id => SeedTenantId ?? (Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("tenant"), out var id) ? id : Guid.Empty);
    public string Actor => accessor.HttpContext?.User.Identity?.Name ?? "demo-seeder";
    public string Role => SeedTenantId.HasValue ? "Admin" : accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? "";
    public void RequireWrite(bool adminOnly = false)
    {
        if (Id == Guid.Empty || (Role != "Admin" && (adminOnly || Role != "Operator")))
            throw new BusinessException("Your role does not allow this action.", 403);
    }
}

public class AppClaimsFactory(UserManager<AppUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("tenant", user.TenantId.ToString()));
        identity.AddClaim(new Claim("company", user.CompanyName));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.AccessRole));
        return identity;
    }
}
