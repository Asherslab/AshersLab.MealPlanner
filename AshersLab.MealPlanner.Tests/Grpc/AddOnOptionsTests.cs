using AshersLab.MealPlanner.Grpc.Features.Auth;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>The add-on's Configuration tab, as the Supervisor writes it to /data/options.json, read into configuration.</summary>
public class AddOnOptionsTests
{
    [Fact]
    public void Each_option_sets_its_auth_key_and_empty_or_unknown_ones_set_nothing()
    {
        IReadOnlyDictionary<string, string?> settings = AddOnOptions.Read("""
            { "trusted_proxies": " 172.30.32.0/23 ", "user_header": "X-Forwarded-User", "display_name_header": "",
              "something_else": "ignored", "Auth:Mode": "None" }
            """);

        Assert.Equal(new Dictionary<string, string?>
        {
            ["Auth:TrustedProxies"] = "172.30.32.0/23",
            ["Auth:UserHeader"]     = "X-Forwarded-User",
        }, settings);
    }

    [Fact]
    public void An_options_file_that_is_not_an_object_sets_nothing() => Assert.Empty(AddOnOptions.Read("[]"));
}
