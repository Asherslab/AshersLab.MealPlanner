using AshersLab.MealPlanner.Grpc.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AshersLab.MealPlanner.Grpc.Features.Accounts;

/// <summary>
/// Someone who can sign in: a name and their passkeys. No password, no email - a passkey is the only way in. Not the
/// household's People (who eats what): those are synced data any device edits; members are who may sync at all, and live
/// only on the server.
/// </summary>
public sealed class Member : IdentityUser<Guid>
{
    /// <summary>The name shown on Settings and on their devices' passkey prompts ("Sam").</summary>
    public string DisplayName { get; set; } = "";

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>"light" or "dark" (<c>AppearanceRules</c>), chosen on Settings → Colours and followed on every device of theirs; null never chose - the light.</summary>
    public string? Appearance { get; set; }
}

/// <summary>
/// A one-time link to set up a passkey: the first member's (printed in the log at start) or someone a member invited.
/// Only the token's hash is stored, so the database alone cannot be used to sign up.
/// </summary>
public sealed class Invitation
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 of the link's token, as hex (<see cref="AccountRules.Hash"/>).</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>The name the inviter gave; null lets the person write their own.</summary>
    public string? ForName { get; set; }

    /// <summary>The member who sent it; null for a setup link from the log.</summary>
    public Guid? InvitedBy { get; set; }

    /// <summary>The member id the passkey is being made for: fixed when the browser asks for creation options.</summary>
    public Guid MemberId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>
/// Members, their passkeys and invitations. The same SQLite file as the sync model - so the backup snapshot holds both -
/// but a context and migration history of its own: Identity's tables are not ops and never sync.
/// </summary>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : IdentityUserContext<Member, Guid>(options)
{
    public const string MigrationsHistoryTable = "__AccountsMigrationsHistory";

    public DbSet<Invitation> Invitations => Set<Invitation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's schema version 3 - the one with the passkeys table - from its options: Accounts.AddAccounts sets it
        // in the host, AccountsDbContextFactory for dotnet ef.
        base.OnModelCreating(builder);
        builder.Entity<Invitation>(e =>
        {
            e.ToTable("Invitation");
            e.HasIndex(i => i.TokenHash).IsUnique();
        });
    }

    /// <summary>The same database file as <see cref="MealPlannerDbContext"/>, with this context's own migration history.</summary>
    public static void Configure(DbContextOptionsBuilder options, IConfiguration configuration) =>
        options.UseSqlite(DataDirectory.ConnectionString(configuration), o => o.MigrationsHistoryTable(MigrationsHistoryTable));
}

/// <summary>For <c>dotnet ef</c>, as <see cref="MealPlannerDbContextFactory"/>.</summary>
public sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AccountsDbContext> options = new();
        AccountsDbContext.Configure(options, new ConfigurationBuilder().AddEnvironmentVariables().Build());
        // The context reads its schema version from Identity's options; without them dotnet ef would leave out the
        // passkeys table. The same setting the host makes.
        options.UseApplicationServiceProvider(new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider());
        return new AccountsDbContext(options.Options);
    }
}
