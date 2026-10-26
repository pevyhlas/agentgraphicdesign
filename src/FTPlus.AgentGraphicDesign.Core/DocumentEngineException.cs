namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// Raised when an engine operation violates a document invariant. Carries a stable
/// machine-readable code so CLI and MCP surfaces can map errors uniformly.
/// </summary>
public sealed class DocumentEngineException : Exception
{
    public DocumentEngineException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable error codes shared by all surfaces.</summary>
public static class EngineErrors
{
    public const string InvalidArguments = "INVALID_ARGUMENTS";
    public const string DocumentNotFound = "DOCUMENT_NOT_FOUND";
    public const string NodeNotFound = "NODE_NOT_FOUND";
    public const string NodeNoBounds = "NODE_NO_BOUNDS";
    public const string ParentNotFound = "PARENT_NOT_FOUND";
    public const string ParentNotContainer = "PARENT_NOT_CONTAINER";
    public const string CannotDeleteRoot = "CANNOT_DELETE_ROOT";
    public const string InvalidNodeType = "INVALID_NODE_TYPE";
    public const string InvalidDimensions = "INVALID_DIMENSIONS";
    public const string InvalidProperty = "INVALID_PROPERTY";
    public const string ExportUnsupported = "EXPORT_UNSUPPORTED";
    public const string RenderUnavailable = "RENDER_UNAVAILABLE";
    public const string StoreUnavailable = "STORE_UNAVAILABLE";
    public const string StoreConflict = "STORE_CONFLICT";
    public const string ParseError = "PARSE_ERROR";
    public const string InvalidId = "INVALID_ID";
    public const string PathEscape = "PATH_ESCAPE";
    public const string RevisionConflict = "REVISION_CONFLICT";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string ResourceLimit = "RESOURCE_LIMIT";
    public const string FontNotFound = "FONT_NOT_FOUND";
    public const string AssetPlatformUnsupported = "ASSET_PLATFORM_UNSUPPORTED";
    public const string MixedParents = "MIXED_PARENTS";
}
