namespace FTPlus.AgentGraphicDesign.Core.Wire;

/// <summary>
/// Raised by <see cref="AgdWireParser"/> when the original wire bytes of a native
/// pixel request (or its MCP tool-call envelope) violate the lexical fidelity rules
/// of the native pixel API contract: duplicate/escaped-duplicate members, unknown
/// members of the closed request envelope, invalid numeric token spelling, or an
/// original-byte size bound. Surfaces map <see cref="Code"/>/<see cref="Path"/>
/// straight into their existing error envelopes; the CLI maps them to its exit path.
/// </summary>
/// <remarks>
/// The carried <see cref="Code"/> reuses the contract's error spellings. The wire
/// seam emits only <c>INVALID_ARGUMENTS</c> (duplicate/unknown members),
/// <c>INVALID_FORMAT</c> (malformed JSON, invalid numeric spelling) and
/// <c>RESOURCE_LIMIT</c> (original-byte size bounds), which is where the contract's
/// resource table classifies the 1&#160;MiB edit-JSON / 1&#160;KiB snapshot caps.
/// </remarks>
public sealed class WireFidelityException : Exception
{
    public WireFidelityException(string code, string message, string? path = null)
        : base(message)
    {
        Code = code;
        Path = path;
    }

    /// <summary>Stable machine-readable error code from the native pixel contract.</summary>
    public string Code { get; }

    /// <summary>Optional schema location (for example <c>$.operations[3].rect</c>), never a filesystem path.</summary>
    public string? Path { get; }
}
