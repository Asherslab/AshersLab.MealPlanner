using System.Diagnostics;

namespace AshersLab.MealPlanner.Tests.Js;

/// <summary>
/// Runs the JavaScript tests for <c>wwwroot/js/shared-database.js</c> (the cross-window transaction gate) so
/// they are part of the one test run. They need Node; without it this test is skipped, not passed.
/// </summary>
public class SharedDatabaseScriptTests
{
    [Fact]
    public async Task Transaction_gate_and_statement_rules_hold()
    {
        string? node = FindNode();
        Assert.SkipWhen(node is null, "node is not installed - run the .test.mjs file with `node --test` where it is.");

        // The file, not the directory: Node 25 treats a directory argument as a single (failing) test.
        string tests = Path.Combine(RepoRoot(), "AshersLab.MealPlanner.Tests", "Js", "shared-database.test.mjs");
        ProcessStartInfo start = new(node!, ["--test", "--test-reporter=spec", tests])
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        using Process process = Process.Start(start)!;
        Task<string>  output  = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string>  errors  = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, $"node --test failed:\n{await output}\n{await errors}");
    }

    private static string? FindNode() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator)
        .Append("/opt/homebrew/bin")
        .Append("/usr/local/bin")
        .Select(dir => Path.Combine(dir, "node"))
        .FirstOrDefault(File.Exists);

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AshersLab.MealPlanner.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above the test binaries.");
    }
}
