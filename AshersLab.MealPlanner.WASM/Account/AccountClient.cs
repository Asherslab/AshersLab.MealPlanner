using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace AshersLab.MealPlanner.WASM.Account;

/// <summary>Who is signed in, as the server says: <c>Available</c> false where the app has no sign-in of its own.</summary>
public sealed record AccountState(bool Available, bool SignedIn, string? Name);

public sealed record PasskeyView(string Id, string? Name, DateTimeOffset CreatedAt, bool Synced);

public sealed record MemberView(Guid Id, string Name, DateTimeOffset JoinedAt, bool IsYou, List<PasskeyView> Passkeys);

public sealed record InvitationView(string? ForName, bool Setup);

public sealed record InvitationCreated(string Token, DateTimeOffset ExpiresAt);

/// <summary>An answer: done, or why not in words to show.</summary>
public sealed record AccountResult(bool Ok, string? Problem, bool Cancelled = false)
{
    public static readonly AccountResult Done = new(true, null);
    public static readonly AccountResult Cancel = new(false, null, Cancelled: true);
}

public sealed record MeBody(bool SignedIn, string? Name);
public sealed record ProblemBody(string Message);
public sealed record EmptyBody;
public sealed record TokenBody(string Token);
public sealed record NameBody(string Token, string? Name);
public sealed record CredentialBody(string CredentialJson);
public sealed record AcceptBody(string Token, string? Name, string CredentialJson);
public sealed record InviteBody(string? ForName);
public sealed record MemberBody(Guid MemberId);
public sealed record PasskeyBody(Guid MemberId, string PasskeyId);

/// <summary>The account endpoints' JSON. Source-generated, as the receipt reader's is, so trimming cannot break it.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MeBody))]
[JsonSerializable(typeof(ProblemBody))]
[JsonSerializable(typeof(EmptyBody))]
[JsonSerializable(typeof(TokenBody))]
[JsonSerializable(typeof(NameBody))]
[JsonSerializable(typeof(CredentialBody))]
[JsonSerializable(typeof(AcceptBody))]
[JsonSerializable(typeof(InviteBody))]
[JsonSerializable(typeof(MemberBody))]
[JsonSerializable(typeof(PasskeyBody))]
[JsonSerializable(typeof(InvitationView))]
[JsonSerializable(typeof(InvitationCreated))]
[JsonSerializable(typeof(List<MemberView>))]
public sealed partial class AccountJson : JsonSerializerContext;

/// <summary>
/// The app's own sign-in, from the browser (docs/modules/infrastructure/accounts.md): the server's <c>/account</c>
/// endpoints, and <c>js/passkeys.js</c> asking the phone for a passkey. Every check is the server's; this only carries.
/// </summary>
/// <remarks>
/// Under Home Assistant or a sign-in proxy there is no <c>/account</c> - a 404 - and <see cref="AccountState.Available"/>
/// is false: the sign-in happens before the app, and the app shows nothing about it.
/// </remarks>
public sealed class AccountClient(HttpClient http, IJSRuntime js)
{
    private const string Offline = "Can't reach the server. Try again with a signal.";

    private IJSObjectReference? _passkeys;

    private async Task<IJSObjectReference> Passkeys() => _passkeys ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/passkeys.js");

    public async Task<AccountState> StateAsync()
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync("account/me");
            if (response.StatusCode == HttpStatusCode.NotFound || response.Content.Headers.ContentType?.MediaType != "application/json")
                return new AccountState(false, false, null);
            MeBody? me = await response.Content.ReadFromJsonAsync(AccountJson.Default.MeBody);
            return new AccountState(true, me?.SignedIn == true, me?.Name);
        }
        catch (HttpRequestException)
        {
            return new AccountState(true, false, null);
        }
        catch (JsonException)
        {
            return new AccountState(false, false, null);   // not this app's answer: a proxy's page, say
        }
    }

    public async Task<bool> SupportedAsync() => await (await Passkeys()).InvokeAsync<bool>("supported");

    // Request options fetched ahead of a tap: Safari only shows the passkey prompt in answer to one, and a round trip to
    // the server between the tap and the prompt can lose it. Each is good for one prompt, and not for long.
    private (string Options, DateTimeOffset At)? _prepared;
    private static readonly TimeSpan PreparedFor = TimeSpan.FromMinutes(3);

    /// <summary>Whether this device has signed in here before, so has a passkey to offer (a hint, kept on the device).</summary>
    public async Task<bool> KnownAsync() => await (await Passkeys()).InvokeAsync<bool>("known");

    /// <summary>Fetches the request options now, so a sign-in on the next tap goes straight to the prompt.</summary>
    public async Task PrepareSignInAsync()
    {
        (string? options, _) = await PostText("account/sign-in/options", new EmptyBody(), AccountJson.Default.EmptyBody);
        _prepared = options is null ? null : (options, DateTimeOffset.UtcNow);
    }

    /// <summary>Signs in with a passkey: the phone asks for Face ID or its PIN.</summary>
    public async Task<AccountResult> SignInAsync()
    {
        string? options = _prepared is { } p && DateTimeOffset.UtcNow - p.At < PreparedFor ? p.Options : null;
        _prepared = null;
        string? problem = null;
        if (options is null)
            (options, problem) = await PostText("account/sign-in/options", new EmptyBody(), AccountJson.Default.EmptyBody);
        if (options is null)
            return new AccountResult(false, problem);
        return await Assert(await (await Passkeys()).InvokeAsync<string?>("get", options));
    }

    private async Task<AccountResult> Assert(string? credential)
    {
        if (credential is null)
            return AccountResult.Cancel;
        AccountResult result = await Post("account/sign-in", new CredentialBody(credential), AccountJson.Default.CredentialBody);
        // A passkey the server does not know (its member removed): stop going straight to the prompt on this device.
        if (result.Ok || result.Problem?.Contains("not one this app knows", StringComparison.Ordinal) == true)
            await (await Passkeys()).InvokeVoidAsync("remember", result.Ok);
        return result;
    }

    private async Task Remember() => await (await Passkeys()).InvokeVoidAsync("remember", true);

    public async Task<(InvitationView? Invitation, string? Problem)> InvitationAsync(string token)
    {
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync("account/invitation", new TokenBody(token), AccountJson.Default.TokenBody);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync(AccountJson.Default.InvitationView), null)
                : (null, await ProblemOf(response));
        }
        catch (HttpRequestException)
        {
            return (null, "Can't reach the server. Open the link again with a signal.");
        }
    }

    /// <summary>Accepts an invitation: makes this person's first passkey, and signs them in.</summary>
    public async Task<AccountResult> AcceptAsync(string token, string? name)
    {
        (string? options, string? problem) = await PostText("account/invitation/options", new NameBody(token, name), AccountJson.Default.NameBody);
        if (options is null)
            return new AccountResult(false, problem);
        if (await (await Passkeys()).InvokeAsync<string?>("create", options) is not { } credential)
            return AccountResult.Cancel;
        AccountResult result = await Post("account/invitation/accept", new AcceptBody(token, name, credential), AccountJson.Default.AcceptBody);
        if (result.Ok)
            await Remember();
        return result;
    }

    /// <summary>Another passkey for whoever is signed in - this device's.</summary>
    public async Task<AccountResult> AddPasskeyAsync()
    {
        (string? options, string? problem) = await PostText("account/passkeys/options", new EmptyBody(), AccountJson.Default.EmptyBody);
        if (options is null)
            return new AccountResult(false, problem);
        if (await (await Passkeys()).InvokeAsync<string?>("create", options) is not { } credential)
            return AccountResult.Cancel;
        AccountResult result = await Post("account/passkeys", new CredentialBody(credential), AccountJson.Default.CredentialBody);
        if (result.Ok)
            await Remember();
        return result;
    }

    public async Task<IReadOnlyList<MemberView>?> MembersAsync()
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync("account/members");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(AccountJson.Default.ListMemberView) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(InvitationCreated? Created, string? Problem)> InviteAsync(string? forName)
    {
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync("account/invitations", new InviteBody(forName), AccountJson.Default.InviteBody);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync(AccountJson.Default.InvitationCreated), null)
                : (null, await ProblemOf(response));
        }
        catch (HttpRequestException)
        {
            return (null, Offline);
        }
    }

    public Task<AccountResult> RemovePasskeyAsync(Guid member, string passkey) =>
        Post("account/passkeys/remove", new PasskeyBody(member, passkey), AccountJson.Default.PasskeyBody);

    public Task<AccountResult> RemoveMemberAsync(Guid member) => Post("account/members/remove", new MemberBody(member), AccountJson.Default.MemberBody);

    public Task<AccountResult> SignOutAsync() => Post("account/sign-out", new EmptyBody(), AccountJson.Default.EmptyBody);

    public Task<AccountResult> SignOutEverywhereAsync() => Post("account/sign-out-everywhere", new EmptyBody(), AccountJson.Default.EmptyBody);

    private async Task<(string? Text, string? Problem)> PostText<T>(string url, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync(url, body, type);
            return response.IsSuccessStatusCode ? (await response.Content.ReadAsStringAsync(), null) : (null, await ProblemOf(response));
        }
        catch (HttpRequestException)
        {
            return (null, Offline);
        }
    }

    private async Task<AccountResult> Post<T>(string url, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync(url, body, type);
            return response.IsSuccessStatusCode ? AccountResult.Done : new AccountResult(false, await ProblemOf(response));
        }
        catch (HttpRequestException)
        {
            return new AccountResult(false, Offline);
        }
    }

    private static async Task<string> ProblemOf(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return "Too many tries just now. Wait a minute and try again.";
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return "You are signed out. Sign in again first.";
        try
        {
            return (await response.Content.ReadFromJsonAsync(AccountJson.Default.ProblemBody))?.Message ?? "That did not work. Try again.";
        }
        catch (Exception)
        {
            return "That did not work. Try again.";
        }
    }
}
