using System.Text;
using System.Xml;

namespace FTPlus.AgentGraphicDesign.Core.Export;

/// <summary>Combines ordered, BOM-free UTF-8 symbol exports without rewriting their XML or IDs.</summary>
public static class SvgSpriteComposer
{
    public const int MaxSymbols = 256;
    public const int MaxOutputBytes = 64 * 1024 * 1024;

    public static byte[] Compose(IReadOnlyList<byte[]> fragments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fragments.Count is 0 or > MaxSymbols)
            throw new DocumentEngineException(EngineErrors.ResourceLimit, $"A sprite requires 1-{MaxSymbols} symbols");
        var prefix = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\">\n<defs>\n"u8.ToArray();
        var suffix = "</defs>\n</svg>\n"u8.ToArray();
        long size = prefix.Length + suffix.Length;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fragment in fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            size += fragment.Length;
            if (size > MaxOutputBytes)
                throw new DocumentEngineException("OUTPUT_TOO_LARGE", "Sprite exceeds the output byte limit");
            try
            {
                if (fragment.AsSpan().StartsWith("\uFEFF"u8))
                    throw new XmlException("A UTF-8 BOM is not allowed in symbol fragments");
                using var stream = new MemoryStream(fragment, writable: false);
                using var text = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                using var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaxOutputBytes
                });
                var rootSeen = false;
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.XmlDeclaration || reader.Depth > 132)
                        throw new XmlException("Expected a bounded symbol fragment without an XML declaration");
                    if (reader.NodeType != XmlNodeType.Element)
                        continue;
                    if (!rootSeen)
                    {
                        if (reader.LocalName != "symbol" || reader.NamespaceURI is not ("" or "http://www.w3.org/2000/svg") ||
                            !SvgExportAdapter.IsValidSymbolId(reader.GetAttribute("id")))
                            throw new XmlException("Expected a symbol with a valid document-name ID");
                        rootSeen = true;
                    }
                    if (!reader.MoveToFirstAttribute())
                        continue;
                    do
                    {
                        if (reader.LocalName == "id" && reader.NamespaceURI is "" or "http://www.w3.org/XML/1998/namespace" &&
                            !ids.Add(reader.Value))
                            throw new DocumentEngineException(EngineErrors.InvalidArguments, $"duplicate element id '{reader.Value}' across sprite documents");
                    } while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                }
                if (!rootSeen)
                    throw new XmlException("Empty symbol fragment");
            }
            catch (Exception ex) when (ex is XmlException or DecoderFallbackException)
            {
                throw new DocumentEngineException("ASSET_EXPORT_FAILED", $"SVG symbol fragments must be BOM-free UTF-8 XML: {ex.Message}");
            }
        }

        var data = new byte[(int)size];
        prefix.CopyTo(data, 0);
        var offset = prefix.Length;
        foreach (var fragment in fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fragment.CopyTo(data, offset);
            offset += fragment.Length;
        }
        suffix.CopyTo(data, offset);
        return data;
    }
}
