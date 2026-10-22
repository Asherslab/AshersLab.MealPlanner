using System.Net;
using System.Net.Http.Json;
using AshersLab.MealPlanner.Grpc.Features.Accounts;
using AshersLab.MealPlanner.Grpc.Features.Auth;
using AshersLab.MealPlanner.Grpc.Features.Ingress;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Server;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>
/// The app's own passkey sign-in (<c>Auth:Mode=Accounts</c>) through the real host: what is refused before anyone signs
/// in, the setup link a fresh install makes, the address rules. The passkey ceremonies themselves are driven in a real
/// browser with a virtual authenticator (.claude/tools/chrome-pwa-check.mjs).
/// </summary>
public class AccountsTests
{
    private const string Url = "https://meals.example.com";

    private static MealPlannerHost Signing(bool production = true)
    {
        MealPlannerHost host = new MealPlannerHost().With("Auth:Mode", "Accounts").With("Auth:PublicUrl", Url);
        return production ? host.InProduction() : host;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Before_anyone_signs_in_every_sync_call_is_a_401()
    {
        await using MealPlannerHost host = Signing();
        RpcException refused = await Assert.ThrowsAsync<RpcException>(() =>
            host.CreateGrpcWebClient<IServerInfoService>(new Dictionary<string, string> { ["Origin"] = Url }).Get(Ct));
        Assert.Equal(StatusCode.Unauthenticated, refused.StatusCode);
    }

    [Fact]
    public async Task A_post_from_another_site_or_with_no_origin_is_refused_before_anything_reads_it()
    {
        await using MealPlannerHost host = Signing();
        HttpClient http = host.CreateClient();

        foreach (string? origin in new[] { "https://evil.example.com", "http://meals.example.com", null })
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "account/sign-in/options") { Content = JsonContent.Create(new { }) };
            if (origin is not null)
                request.Headers.Add("Origin", origin);
            using HttpResponseMessage response = await http.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using HttpRequestMessage ours = new(HttpMethod.Post, "account/sign-in/options") { Content = JsonContent.Create(new { }) };
        ours.Headers.Add("Origin", Url);
        using HttpResponseMessage options = await http.SendAsync(ours, Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        // The request options name this app's host as the passkeys' relying party.
        Assert.Contains("\"rpId\":\"meals.example.com\"", await options.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Nobody_is_signed_in_and_the_member_pages_say_so_with_a_401()
    {
        await using MealPlannerHost host = Signing();
        HttpClient http = host.CreateClient();
        Assert.Equal("""{"signedIn":false,"name":null,"appearance":null}""", await http.GetStringAsync("account/me", Ct));
        using HttpResponseMessage members = await http.GetAsync("account/members", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, members.StatusCode);
    }

    [Fact]
    public async Task Light_or_dark_is_kept_only_for_someone_signed_in()
    {
        await using MealPlannerHost host = Signing();
        HttpClient http = host.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "account/appearance") { Content = JsonContent.Create(new { appearance = "dark" }) };
        request.Headers.Add("Origin", Url);
        using HttpResponseMessage response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_member_who_never_chose_has_no_appearance_and_the_column_takes_one()
    {
        await using MealPlannerHost host = Signing();
        _ = host.CreateClient();
        using IServiceScope scope = host.Services.CreateScope();
        AccountsDbContext db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        Member member = new() { Id = Guid.CreateVersion7(), UserName = "a", DisplayName = "Sam", JoinedAt = DateTimeOffset.UtcNow };
        db.Users.Add(member);
        await db.SaveChangesAsync(Ct);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(m => m.Id == member.Id, Ct)).Appearance);

        member.Appearance = "dark";
        await db.SaveChangesAsync(Ct);
        Assert.Equal("dark", (await db.Users.AsNoTracking().SingleAsync(m => m.Id == member.Id, Ct)).Appearance);
    }

    [Fact]
    public async Task A_fresh_install_makes_one_setup_link_stored_as_a_hash()
    {
        await using MealPlannerHost host = Signing();
        _ = host.CreateClient();
        Invitation first = Assert.Single(await Invitations(host));
        Assert.Null(first.InvitedBy);
        Assert.Equal(64, first.TokenHash.Length);   // a hash, never the token
        Assert.True(first.ExpiresAt - first.CreatedAt == AccountRules.SetupLifetime);
    }

    [Fact]
    public async Task Without_the_apps_own_sign_in_account_is_a_404_not_the_shell()
    {
        await using MealPlannerHost host = new();   // development, no sign-in
        using HttpResponseMessage response = await host.CreateClient().GetAsync("account/me", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_invitation_code_that_is_not_one_is_refused()
    {
        await using MealPlannerHost host = Signing();
        HttpClient http = host.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "account/invitation") { Content = JsonContent.Create(new { token = AccountRules.NewToken() }) };
        request.Headers.Add("Origin", Url);
        using HttpResponseMessage response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not one this app made", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("https://meals.example.com", null)]
    [InlineData("https://meals.example.com/", null)]
    [InlineData("http://localhost:18410", null)]
    [InlineData("http://meals.example.com", "must be https")]
    [InlineData("https://meals.example.com/app", "nothing after the host")]
    [InlineData("meals.example.com", "is not an address")]
    [InlineData("", "is not an address")]
    public void The_public_address_is_https_and_nothing_after_the_host(string url, string? problem)
    {
        Uri? parsed = AccountRules.PublicUrl(url, out string? why);
        Assert.Equal(problem is null, parsed is not null);
        if (problem is not null)
            Assert.Contains(problem, why);
    }

    [Fact]
    public void Accounts_need_a_public_address_or_the_host_does_not_start()
    {
        AuthOptions options = new() { Mode = "Accounts", PublicUrl = "http://meals.example.com" };
        Assert.Null(options.Resolve(new IngressOptions(), development: false, out string? problem));
        Assert.Contains("PublicUrl must be https", problem);
        Assert.Equal(AuthMode.Accounts, new AuthOptions { Mode = "Accounts", PublicUrl = Url }.Resolve(new IngressOptions(), false, out _));
    }

    [Fact]
    public void Links_are_random_stored_as_hashes_and_used_once_before_they_expire()
    {
        string a = AccountRules.NewToken(), b = AccountRules.NewToken();
        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length);   // 32 bytes, base64url, no padding
        Assert.DoesNotContain('+', a + b);
        Assert.Equal(AccountRules.Hash(a), AccountRules.Hash(a));
        Assert.NotEqual(a, AccountRules.Hash(a));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Invitation live = new() { ExpiresAt = now.AddHours(1) };
        Assert.Null(AccountRules.InvitationProblem(live, now));
        Assert.Contains("expired", AccountRules.InvitationProblem(live, now.AddHours(1)));
        Assert.Contains("used already", AccountRules.InvitationProblem(new Invitation { ExpiresAt = now.AddHours(1), UsedAt = now }, now));
        Assert.Contains("not one this app made", AccountRules.InvitationProblem(null, now));
    }

    private static async Task<List<Invitation>> Invitations(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Invitations.AsNoTracking().ToListAsync(Ct);
    }
}
