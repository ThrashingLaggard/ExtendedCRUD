using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CRUDimplementation.Auth
{
    /// <summary>
    /// Demo-only authentication handler: every request is treated as authenticated, with the
    /// role taken from an <c>X-Demo-Role</c> header (defaulting to "user").
    /// </summary>
    /// <remarks>
    /// This exists purely so the CRUD+ "CrudRead"/"CrudWrite" policies (see CRUD+'s README) have
    /// something real to evaluate against without standing up a full login flow. It runs through
    /// the genuine ASP.NET Core authentication/authorization pipeline — it is not a bypass — but
    /// it must never be used outside this local demo: anyone can claim any role via a header.
    /// </remarks>
    public class DemoAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Demo";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string role = Request.Headers["X-Demo-Role"].FirstOrDefault() ?? "user";

            Claim[] claims =
            [
                new Claim(ClaimTypes.Name, "demo-user"),
                new Claim(ClaimTypes.Role, role),
            ];
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
