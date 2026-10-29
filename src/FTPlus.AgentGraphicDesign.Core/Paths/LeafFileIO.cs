namespace FTPlus.AgentGraphicDesign.Core.Paths;

/// <summary>Real leaf-open boundary; internal call-local delegates can observe attempts in tests.</summary>
internal static class LeafFileIO
{
    internal static Stream Open(string path, FileStreamOptions options) => new FileStream(path, options);
}
