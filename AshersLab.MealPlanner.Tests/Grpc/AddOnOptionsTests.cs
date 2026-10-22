using AshersLab.MealPlanner.Grpc.Features.Auth;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>The add-on's Configuration tab, as the Supervisor writes it to /data/options.json, read into configuration.</summary>
public class AddOnOptionsTests
{
    [Fact]
    public void Each_option_sets_its_auth_key_and_empty_or_unknown_ones_set_nothing()
    {
        IReadOnlyDictionary<string, string?> settings = AddOnOptions.Read("""
            { "public_url": " https://meals.example.com ", "reset_access": false,
              "something_else": "ignored", "Auth:Mode": "None", "mode": "None" }
            """);

        Assert.Equal(new Dictionary<string, string?>
        {
            ["Auth:PublicUrl"]   = "https://meals.example.com",
            ["Auth:ResetAccess"] = "false",
        }, settings);
    }

    [Fact]
    public void An_empty_address_sets_nothing_so_the_host_says_it_is_missing()
    {
        Assert.Equal(new Dictionary<string, string?> { ["Auth:ResetAccess"] = "true" },
            AddOnOptions.Read("""{ "public_url": "", "reset_access": true }"""));
        Assert.Empty(AddOnOptions.Read("[]"));
    }
}
