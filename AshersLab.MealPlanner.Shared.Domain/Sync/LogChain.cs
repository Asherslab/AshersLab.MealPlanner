using System.Security.Cryptography;
using System.Text;

namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// A fingerprint of the server's op log up to and including one op: the hash of the previous op's chain and
/// this op's id. Two logs with the same chain at a seq hold the same ops in the same order up to it.
/// </summary>
/// <remarks>
/// Why a seq alone is not enough: the server is restored from Home Assistant backups, and a restored log
/// numbers new ops from the backup's head again, so "seq 41" can name a different op than the one a browser
/// pulled as seq 41. A browser sends the chain of its cursor with every pull; the server answers a mismatch
/// with a snapshot that tells the browser history was lost (docs/modules/sync/offline-sync.md).
/// </remarks>
public static class LogChain
{
    /// <summary>The chain before the first op.</summary>
    public const string Empty = "";

    /// <summary>32 lowercase hex characters.</summary>
    public static string Next(string previous, Guid opId)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes($"{previous}|{opId:N}"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
