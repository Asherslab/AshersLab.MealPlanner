using System.Security.Claims;
using System.Threading.RateLimiting;
using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Features.Auth;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Shared.Domain.Appearance;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AshersLab.MealPlanner.Grpc.Features.Accounts;

/// <summary>
/// The app's own sign-in, with passkeys and nothing else (<c>Auth:Mode=Accounts</c>; docs/modules/infrastructure/accounts.md).
/// ASP.NET Core Identity does the WebAuthn ceremonies and stores the passkeys; this is the rest: who may sign up (a
/// one-time link - the first from the log, the rest from a member), the session cookie, and the endpoints the WASM app's
/// sign-in pages call.
/// </summary>
/// <remarks>
/// What protects the household's data, in order: <b>every gRPC call needs a signed-in member</b> (an unsigned one is a
/// 401, which the app shows as "Signed out"); the session cookie is HttpOnly, Secure and SameSite=Strict; <b>every POST
/// must come from the public address's origin</b> (a cross-site request is a 403 before anything reads it); passkeys and
/// their ceremonies are bound to the public address's host and origin; sign-in and sign-up are rate-limited; and links are
/// stored only as hashes. The app shell and its files are public - they hold no data; a phone's own copy is its own.
/// </remarks>
public static class Accounts
{
    public const string RateLimit = "sign-in";

    /// <summary>The session cookie's name.</summary>
    public const string Cookie = "mealplanner.session";

    public static void AddAccounts(this WebApplicationBuilder builder, AuthOptions auth)
    {
        Uri url = AccountRules.PublicUrl(auth.PublicUrl, out _)!;
        string origin = AccountRules.Origin(url);

        builder.Services.AddDbContext<AccountsDbContext>((sp, o) => AccountsDbContext.Configure(o, sp.GetRequiredService<IConfiguration>()));

        // The keys that sign the session cookie, beside the database: in /data, so a restart - or a restore - keeps
        // everyone signed in. Without this every restart of the add-on would sign out every phone.
        string keys = Path.Combine(Path.GetDirectoryName(DataDirectory.DatabasePath(builder.Configuration))!, "keys");
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("AshersLab.MealPlanner");

        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.AddIdentityCore<Member>(o =>
            {
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;   // the passkeys table
                o.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789-";   // the member's id
            })
            .AddEntityFrameworkStores<AccountsDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<MemberClaims>();

        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name         = Cookie;
            o.Cookie.HttpOnly     = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite     = SameSiteMode.Strict;
            o.ExpireTimeSpan      = AccountRules.Session;
            o.SlidingExpiration   = true;
            // An API, not pages: no redirects to a login page that does not exist - a plain 401 or 403.
            o.Events.OnRedirectToLogin        = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        // The ceremonies' short-lived state cookies: also secure, also this site only.
        builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite     = SameSiteMode.Strict;
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = AccountRules.RecheckEvery);
        builder.Services.Configure<IdentityPasskeyOptions>(o =>
        {
            o.ServerDomain               = url.Host;
            o.UserVerificationRequirement = "required";   // Face ID, a fingerprint or the phone's PIN - not just "a tap"
            o.ResidentKeyRequirement     = "required";    // discoverable: sign in without typing a name
            o.ValidateOrigin             = c => ValueTask.FromResult(!c.CrossOrigin && c.Origin == origin);
        });
        builder.Services.AddAuthorization();

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // One bucket for everyone: two people sign in a few times a season, so 20 a minute is far above use and far
            // below guessing. Not per address - behind the proxy every request comes from the proxy.
            o.AddFixedWindowLimiter(RateLimit, l => { l.PermitLimit = 20; l.Window = TimeSpan.FromMinutes(1); l.QueueLimit = 0; });
        });
    }

    /// <summary>The cross-site check and the session, ahead of every file and service; then the rate limiter after routing.</summary>
    public static void UseAccounts(this WebApplication app, AuthOptions auth)
    {
        string origin = AccountRules.Origin(AccountRules.PublicUrl(auth.PublicUrl, out _)!);

        // A POST from another site is refused before anything reads it. Browsers send Origin on every POST fetch,
        // same-origin included - so one with no Origin is not this app's page either.
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers.Origin.ToString() != origin)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Not from this app's own address.", context.RequestAborted);
                return;
            }
            await next(context);
        });
        app.UseAuthentication();
    }

    /// <summary>After <c>UseRouting</c>: rate limits and the authorization the gRPC services require.</summary>
    public static void UseAccountsAfterRouting(this WebApplication app)
    {
        app.UseRateLimiter();
        app.UseAuthorization();
    }

    /// <summary>Migrates the accounts tables, and with nobody able to sign in prints a setup link to the log.</summary>
    public static async Task StartAsync(WebApplication app, AuthOptions auth)
    {
        using IServiceScope scope = app.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await db.Database.MigrateAsync();

        bool anyone = await db.Users.AnyAsync();
        if (anyone && !auth.ResetAccess)
            return;

        // Earlier setup links stop working: only the newest one printed is live.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (Invitation old in await db.Invitations.Where(i => i.InvitedBy == null && i.UsedAt == null).ToListAsync())
            db.Invitations.Remove(old);
        string token = AccountRules.NewToken();
        db.Invitations.Add(new Invitation
        {
            Id = Guid.CreateVersion7(), TokenHash = AccountRules.Hash(token), CreatedAt = now, ExpiresAt = now + AccountRules.SetupLifetime,
        });
        await db.SaveChangesAsync();

        Uri url = AccountRules.PublicUrl(auth.PublicUrl, out _)!;
        app.Logger.LogWarning(
            "{Who} Open this link to set up a passkey (it works once, until {Until:yyyy-MM-dd HH:mm} UTC): {Link}{After}",
            anyone ? "reset_access is on." : "Nobody can sign in yet.", now + AccountRules.SetupLifetime,
            new Uri(url, $"welcome#{token}"), anyone ? " Turn reset_access off once you are in." : "");
    }

    public static void MapAccounts(this WebApplication app)
    {
        RouteGroupBuilder account = app.MapGroup("/account");

        account.MapGet("/me", async (HttpContext http, UserManager<Member> users) =>
            await users.GetUserAsync(http.User) is { } me
                ? Results.Ok(new MeResponse(true, me.DisplayName, me.Appearance))
                : Results.Ok(new MeResponse(false, null, null)));

        // --- Signing in ---------------------------------------------------------------------------------------------------
        account.MapPost("/sign-in/options", async (SignInManager<Member> signIn) =>
            Results.Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json")).RequireRateLimiting(RateLimit);

        account.MapPost("/sign-in", async (CredentialRequest request, SignInManager<Member> signIn, UserManager<Member> users) =>
        {
            PasskeyAssertionResult<Member> result = await signIn.PerformPasskeyAssertionAsync(request.CredentialJson);
            if (!result.Succeeded)
                return Results.Json(new Problem("That passkey is not one this app knows."), statusCode: StatusCodes.Status401Unauthorized);
            await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey);   // its sign count: a cloned key is caught next time
            await signIn.SignInAsync(result.User, isPersistent: true);
            return Results.Ok(new MeResponse(true, result.User.DisplayName, result.User.Appearance));
        }).RequireRateLimiting(RateLimit);

        // --- An invitation: a new member and their first passkey ------------------------------------------------------------
        account.MapPost("/invitation", async (TokenRequest request, AccountsDbContext db) =>
        {
            Invitation? invitation = await Find(db, request.Token);
            return AccountRules.InvitationProblem(invitation, DateTimeOffset.UtcNow) is { } problem
                ? Results.Json(new Problem(problem), statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(new InvitationResponse(invitation!.ForName, invitation.InvitedBy is null));
        }).RequireRateLimiting(RateLimit);

        account.MapPost("/invitation/options", async (InvitationRequest request, AccountsDbContext db, SignInManager<Member> signIn) =>
        {
            Invitation? invitation = await Find(db, request.Token);
            string name = Shared.Domain.Names.Normalise(invitation?.ForName ?? request.Name);
            if ((AccountRules.InvitationProblem(invitation, DateTimeOffset.UtcNow) ?? AccountRules.NameProblem(name)) is { } problem)
                return Results.Json(new Problem(problem), statusCode: StatusCodes.Status400BadRequest);
            if (invitation!.MemberId == Guid.Empty)
            {
                invitation.MemberId = Guid.CreateVersion7();
                await db.SaveChangesAsync();
            }
            return Results.Content(await signIn.MakePasskeyCreationOptionsAsync(Entity(invitation.MemberId, name)), "application/json");
        }).RequireRateLimiting(RateLimit);

        account.MapPost("/invitation/accept", async (AcceptRequest request, AccountsDbContext db, SignInManager<Member> signIn, UserManager<Member> users) =>
        {
            Invitation? invitation = await Find(db, request.Token);
            string name = Shared.Domain.Names.Normalise(invitation?.ForName ?? request.Name);
            if ((AccountRules.InvitationProblem(invitation, DateTimeOffset.UtcNow) ?? AccountRules.NameProblem(name)) is { } problem)
                return Results.Json(new Problem(problem), statusCode: StatusCodes.Status400BadRequest);

            PasskeyAttestationResult result = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson);
            // The passkey must be the one made for this invitation's member - never attached to anyone else.
            if (!result.Succeeded || result.UserEntity.Id != invitation!.MemberId.ToString())
                return Results.Json(new Problem("The passkey could not be saved. Try again."), statusCode: StatusCodes.Status400BadRequest);

            Member member = new() { Id = invitation.MemberId, UserName = invitation.MemberId.ToString(), DisplayName = name, JoinedAt = DateTimeOffset.UtcNow };
            if (!(await users.CreateAsync(member)).Succeeded || !(await users.AddOrUpdatePasskeyAsync(member, result.Passkey)).Succeeded)
                return Results.Json(new Problem("This link has been used already. Ask for a new one."), statusCode: StatusCodes.Status400BadRequest);
            invitation.UsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await signIn.SignInAsync(member, isPersistent: true);
            return Results.Ok(new MeResponse(true, member.DisplayName, member.Appearance));
        }).RequireRateLimiting(RateLimit);

        // --- Signed in: the household's members, invitations, passkeys, signing out ------------------------------------------
        RouteGroupBuilder members = account.MapGroup("").RequireAuthorization();

        members.MapGet("/members", async (HttpContext http, AccountsDbContext db, UserManager<Member> users) =>
        {
            Guid me = MemberId(http.User);
            List<MemberResponse> list = [];
            foreach (Member member in (await db.Users.ToListAsync()).OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                list.Add(new MemberResponse(member.Id, member.DisplayName, member.JoinedAt, member.Id == me,
                    [.. (await users.GetPasskeysAsync(member)).OrderBy(p => p.CreatedAt)
                        .Select(p => new PasskeyResponse(Base64Url(p.CredentialId), p.Name, p.CreatedAt, p.IsBackedUp))]));
            return Results.Ok(list);
        });

        members.MapPost("/invitations", async (InviteRequest request, HttpContext http, AccountsDbContext db) =>
        {
            string? forName = request.ForName is { } n && Shared.Domain.Names.Normalise(n) is { Length: > 0 } name ? name : null;
            if (forName is not null && AccountRules.NameProblem(forName) is { } problem)
                return Results.Json(new Problem(problem), statusCode: StatusCodes.Status400BadRequest);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string token = AccountRules.NewToken();
            db.Invitations.Add(new Invitation
            {
                Id = Guid.CreateVersion7(), TokenHash = AccountRules.Hash(token), ForName = forName, InvitedBy = MemberId(http.User),
                CreatedAt = now, ExpiresAt = now + AccountRules.InvitationLifetime,
            });
            await db.SaveChangesAsync();
            return Results.Ok(new InvitationCreated(token, now + AccountRules.InvitationLifetime));
        });

        // Another passkey for yourself: a phone whose passkeys do not sync with the first.
        members.MapPost("/passkeys/options", async (HttpContext http, UserManager<Member> users, SignInManager<Member> signIn) =>
            await users.GetUserAsync(http.User) is { } me
                ? Results.Content(await signIn.MakePasskeyCreationOptionsAsync(Entity(me.Id, me.DisplayName)), "application/json")
                : Results.Unauthorized());

        members.MapPost("/passkeys", async (CredentialRequest request, HttpContext http, UserManager<Member> users, SignInManager<Member> signIn) =>
        {
            if (await users.GetUserAsync(http.User) is not { } me)
                return Results.Unauthorized();
            PasskeyAttestationResult result = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson);
            if (!result.Succeeded || result.UserEntity.Id != me.Id.ToString())
                return Results.Json(new Problem("The passkey could not be saved. Try again."), statusCode: StatusCodes.Status400BadRequest);
            await users.AddOrUpdatePasskeyAsync(me, result.Passkey);
            return Results.Ok();
        });

        members.MapPost("/passkeys/remove", async (RemovePasskeyRequest request, AccountsDbContext db, UserManager<Member> users) =>
        {
            if (await users.FindByIdAsync(request.MemberId.ToString()) is not { } member || FromBase64Url(request.PasskeyId) is not { } id)
                return Results.NotFound();
            // A member's last passkey goes with the member (below), never alone: a member with no way in is a dead end.
            if ((await users.GetPasskeysAsync(member)).Count <= 1)
                return Results.Json(new Problem($"That is {member.DisplayName}'s only passkey. Remove {member.DisplayName} instead, and invite them again."),
                    statusCode: StatusCodes.Status400BadRequest);
            await users.RemovePasskeyAsync(member, id);
            await users.UpdateSecurityStampAsync(member);   // a session that signed in with it ends within a minute
            return Results.Ok();
        });

        members.MapPost("/members/remove", async (MemberRequest request, AccountsDbContext db, UserManager<Member> users) =>
        {
            if (await users.FindByIdAsync(request.MemberId.ToString()) is not { } member)
                return Results.NotFound();
            if (await db.Users.CountAsync() <= 1)
                return Results.Json(new Problem("The last person who can sign in cannot be removed."), statusCode: StatusCodes.Status400BadRequest);
            await users.DeleteAsync(member);   // their sessions end at the next check: the member is gone
            return Results.Ok();
        });

        // Light or dark: yours, on every device of yours (Shared.Domain/Appearance).
        members.MapPost("/appearance", async (AppearanceRequest request, HttpContext http, UserManager<Member> users) =>
        {
            if (!AppearanceRules.Valid(request.Appearance))
                return Results.Json(new Problem("Choose light or dark."), statusCode: StatusCodes.Status400BadRequest);
            if (await users.GetUserAsync(http.User) is not { } me)
                return Results.Unauthorized();
            me.Appearance = request.Appearance;
            IdentityResult saved = await users.UpdateAsync(me);
            return saved.Succeeded
                ? Results.Ok()
                : Results.Json(new Problem(string.Join(" ", saved.Errors.Select(e => e.Description))), statusCode: StatusCodes.Status409Conflict);
        });

        members.MapPost("/sign-out", async (SignInManager<Member> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok();
        });

        // Every device of yours, this one included: the stamp changes, and each session fails its next check.
        members.MapPost("/sign-out-everywhere", async (HttpContext http, UserManager<Member> users, SignInManager<Member> signIn) =>
        {
            if (await users.GetUserAsync(http.User) is { } me)
                await users.UpdateSecurityStampAsync(me);
            await signIn.SignOutAsync();
            return Results.Ok();
        });
    }

    private static async Task<Invitation?> Find(AccountsDbContext db, string? token) =>
        token is { Length: > 20 and < 100 } ? await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == AccountRules.Hash(token)) : null;

    private static PasskeyUserEntity Entity(Guid id, string name) => new() { Id = id.ToString(), Name = name, DisplayName = name };

    private static Guid MemberId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : Guid.Empty;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string? text)
    {
        if (text is null)
            return null;
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        try { return Convert.FromBase64String(padded); }
        catch (FormatException) { return null; }
    }

    public sealed record MeResponse(bool SignedIn, string? Name, string? Appearance);
    public sealed record AppearanceRequest(string? Appearance);
    public sealed record Problem(string Message);
    public sealed record CredentialRequest(string CredentialJson);
    public sealed record TokenRequest(string? Token);
    public sealed record InvitationRequest(string? Token, string? Name);
    public sealed record AcceptRequest(string? Token, string? Name, string CredentialJson);
    public sealed record InvitationResponse(string? ForName, bool Setup);
    public sealed record InviteRequest(string? ForName);
    public sealed record InvitationCreated(string Token, DateTimeOffset ExpiresAt);
    public sealed record MemberRequest(Guid MemberId);
    public sealed record RemovePasskeyRequest(Guid MemberId, string? PasskeyId);
    public sealed record PasskeyResponse(string Id, string? Name, DateTimeOffset CreatedAt, bool Synced);
    public sealed record MemberResponse(Guid Id, string Name, DateTimeOffset JoinedAt, bool IsYou, IReadOnlyList<PasskeyResponse> Passkeys);
}

/// <summary>A member's claims: the display name as Home Assistant's and the sign-in proxy's are, so the Server card reads the same.</summary>
public sealed class MemberClaims(UserManager<Member> users, IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<Member>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Member user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(IngressRequest.DisplayNameClaim, user.DisplayName));
        return identity;
    }
}
