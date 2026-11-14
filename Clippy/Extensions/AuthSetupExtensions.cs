using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
namespace Clippy.Extensions;
public static class AuthSetupExtensions
{
    public static IServiceCollection AddClippyAuthentication(this IServiceCollection services,IConfiguration configuration)
    {
        services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 10;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 10;
            options.Lockout.AllowedForNewUsers = true;
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedAccount = false;
        });
        var authBuilder = services.AddAuthentication();
        var googleClientId = configuration["Authentication:Google:ClientId"];
        var googleClientSecret = configuration["Authentication:Google:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
        {
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleClientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = true;
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "given_name");
                options.ClaimActions.MapJsonKey(ClaimTypes.Surname,"family_name");
                options.ClaimActions.MapJsonKey("urn:google:picture", "picture", "url");
            });
        }
        var metaAppId = configuration["Authentication:Meta:AppId"];
        var metaAppSecret = configuration["Authentication:Meta:AppSecret"];
        if(!string.IsNullOrWhiteSpace(metaAppId) && !string.IsNullOrWhiteSpace(metaAppSecret))
        {
            authBuilder.AddFacebook(options =>
            {
                options.AppId = metaAppId;
                options.AppSecret = metaAppSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = true;
                options.CallbackPath = "/signin-facebook";
                options.Scope.Add("email");
                options.Scope.Add("public_profile");
                options.Fields.Add("id");
                options.Fields.Add("name");
                options.Fields.Add("email");
                options.Fields.Add("first_name");
                options.Fields.Add("last_name");
                options.Fields.Add("picture");
            });
        }
        var oidcAuthority = configuration["Authentication:OIDC:Authority"];
        var oidcClientId = configuration["Authentication:OIDC:ClientId"];
        var oidcClientSecret = configuration["Authentication:OIDC:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(oidcAuthority) && !string.IsNullOrWhiteSpace(oidcClientId) && !string.IsNullOrWhiteSpace(oidcClientSecret))
        {
            authBuilder.AddOpenIdConnect("oidc", "Enterprise SSO (OpenId connect)", options =>
            {
                options.Authority = oidcAuthority;
                options.ClientId = oidcClientId;
                options.ClientSecret = oidcClientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.UsePkce = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.CallbackPath = "/signin-oidc";
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
            });
        }
        return services;
    }
}
