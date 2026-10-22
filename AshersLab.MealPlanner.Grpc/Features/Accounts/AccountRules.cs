using System.Security.Cryptography;
using AshersLab.MealPlanner.Shared.Domain;

namespace AshersLab.MealPlanner.Grpc.Features.Accounts;

/// <summary>The pure rules of signing in: tokens, how long things last, names, and the public address.</summary>
public static class AccountRules
{
    /// <summary>A session lasts this long without use; every use renews it. A phone in daily use stays signed in.</summary>
    public static readonly TimeSpan Session = TimeSpan.FromDays(90);

    /// <summary>An invitation a member sends lasts a day.</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(1);

    /// <summary>The setup link in the log lasts a week - the add-on may start long before anyone opens it.</summary>
    public static readonly TimeSpan SetupLifetime = TimeSpan.FromDays(7);

    /// <summary>How soon "sign out everywhere" or a removed member reaches a device's open session.</summary>
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(1);

    /// <summary>A link's token: 32 random bytes, URL-safe. Only <see cref="Hash"/> of it is stored.</summary>
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The stored form of a token: SHA-256, as lower-case hex.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Why an invitation cannot be used now, or null.</summary>
    public static string? InvitationProblem(Invitation? invitation, DateTimeOffset now) => invitation switch
    {
        null                          => "This link is not one this app made.",
        { UsedAt: not null }          => "This link has been used already. Ask for a new one.",
        _ when now >= invitation.ExpiresAt => "This link has expired. Ask for a new one.",
        _                             => null,
    };

    /// <summary>Why a member's name will not do, or null. A name as the app's other names: trimmed, short, printable.</summary>
    public static string? NameProblem(string? name) => Names.Problem(Names.Normalise(name), "Each person");

    /// <summary>
    /// The address people open, from configuration: https, or http only on localhost (development, the simulation), and
    /// nothing after the host. Passkeys are bound to its host for good - change it and every passkey stops working.
    /// </summary>
    public static Uri? PublicUrl(string? configured, out string? problem)
    {
        problem = null;
        if (!Uri.TryCreate(configured?.Trim(), UriKind.Absolute, out Uri? url))
            problem = "is not an address like https://meals.example.com";
        else if (url.Scheme != Uri.UriSchemeHttps && !(url.Scheme == Uri.UriSchemeHttp && url.IsLoopback))
            problem = "must be https (http only on localhost)";
        else if (url.AbsolutePath != "/" || url.Query.Length > 0 || url.Fragment.Length > 0 || url.UserInfo.Length > 0)
            problem = "must be the address alone, with nothing after the host";
        return problem is null ? url : null;
    }

    /// <summary>"https://meals.example.com" - how a browser writes this address's origin (no default port, no slash).</summary>
    public static string Origin(Uri url) => url.GetLeftPart(UriPartial.Authority);
}
