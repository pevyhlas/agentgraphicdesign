using System.Security.Cryptography;
using System.Text;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// Generates deterministic GUIDs from a seed and purpose string.
/// Uses SHA256 to hash inputs and extracts 16 bytes for the GUID.
/// </summary>
internal static class DeterministicGuid
{
    public static Guid Generate(int seed, string purpose)
    {
        var input = $"agd:{seed}:{purpose}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = hash[..16];
        return new Guid(guidBytes);
    }
}
