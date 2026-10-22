using System.Globalization;
using System.Text.Json;
using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Ordering;
using FTPlus.AgentGraphicDesign.Core.Patching;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// The single shared engine behind every surface (CLI, MCP, HTTP host). Owns
/// document lifecycle, queries, invariant-enforcing mutations, and export/render
/// orchestration. Clients stay thin: parse input, call the engine, format output.
/// </summary>
public sealed class DocumentEngine : IDocumentEngine
{
    private readonly IDocumentStore? _store;
    private readonly IRenderer? _renderer;
    private readonly IContactSheetRenderer? _contactSheetRenderer;
    private readonly ITextMeasurer _textMeasurer;
    private readonly Func<FontPolicy, ITextMeasurer>? _measurerFactory;
    private readonly Dictionary<string, IExportAdapter> _exportAdapters;
    private readonly PathOperationsService _pathOps = new();
    private readonly TokenService _tokenService = new();

    /// <param name="store">Store for ID-based operations; omit for purely file-based use.</param>
    /// <param name="exportAdapters">Export adapters keyed by <see cref="IExportAdapter.Id"/>; defaults to SVG.</param>
    /// <param name="renderer">Raster renderer; render calls fail cleanly when omitted.</param>
    /// <param name="textMeasurer">Default-policy text measurer for bounds; the glyph-count estimator is used when omitted.</param>
    /// <param name="contactSheetRenderer">Grid compositor for contact sheets; contact-sheet calls fail cleanly when omitted.</param>
    /// <param name="measurerFactory">
    /// Builds a text measurer bound to a caller-supplied <see cref="FontPolicy"/> so bounds,
    /// layout, and lint can resolve fonts exactly as a policy-bound render would. When omitted,
    /// a requested policy is ignored and the default <paramref name="textMeasurer"/> is used.
    /// </param>
    public DocumentEngine(
        IDocumentStore? store = null,
        IEnumerable<IExportAdapter>? exportAdapters = null,
        IRenderer? renderer = null,
        ITextMeasurer? textMeasurer = null,
        IContactSheetRenderer? contactSheetRenderer = null,
        Func<FontPolicy, ITextMeasurer>? measurerFactory = null)
    {
        _store = store;
        _renderer = renderer;
        _contactSheetRenderer = contactSheetRenderer;
        _textMeasurer = textMeasurer ?? EstimatedTextMeasurer.Instance;
        _measurerFactory = measurerFactory;
        _exportAdapters = (exportAdapters ?? [new SvgExportAdapter(_textMeasurer)])
            .ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    // ---- Document lifecycle ----

    public Document CreateDocument(string name, double width = 1920, double height = 1080, string? background = null)
        => CreateDocument(name, width, height, background, options: null);

    public Document CreateDocument(string name, double width, double height, string? background, CreateDocumentOptions? options)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                "Document name cannot be blank");
        }

        ValidateStringLength(name, DocumentLimits.MaxNameLength, "Document name");

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Canvas dimensions must be positive; got {width}x{height}");
        }

        if (background is not null && !DocumentLimits.IsValidColor(background))
        {
            throw new DocumentEngineException(EngineErrors.InvalidProperty,
                $"Invalid canvas background color: '{background}'");
        }

        var documentId = options?.DocumentId
            ?? (options?.Seed is int seed ? DeterministicGuid.Generate(seed, "document") : Guid.NewGuid());
        var rootId = options?.RootId
            ?? (options?.Seed is int rootSeed ? DeterministicGuid.Generate(rootSeed, "root") : Guid.NewGuid());

        if (documentId == Guid.Empty || rootId == Guid.Empty || documentId == rootId)
        {
            throw new DocumentEngineException(EngineErrors.InvalidId,
                "Document and root IDs must be non-empty and distinct");
        }

        return new Document
        {
            Schema = Document.SchemaUrl,
            Id = documentId,
            Name = name,
            Version = 1,
            Revn = 1,
            Canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = background
            },
            Root = new FrameNode
            {
                Id = rootId,
                Name = "root",
                Width = width,
                Height = height,
                Children = []
            }
        };
    }

    public Document? LoadDocument(Guid id) => LoadDocument(id, CancellationToken.None);

    public Document? LoadDocument(Guid id, CancellationToken cancellationToken) =>
        RequireStore().Load(id, cancellationToken)?.Document;

    public Document? LoadDocumentFromFile(string filePath) =>
        LoadDocumentFromFile(filePath, CancellationToken.None);

    public Document? LoadDocumentFromFile(string filePath, CancellationToken cancellationToken)
    {
        var result = TryLoadDocumentFromFile(filePath, cancellationToken);
        return result.Document;
    }

    /// <summary>
    /// Load document from file with structured error reporting. Returns parse error
    /// details instead of null when deserialization fails.
    /// </summary>
    public DeserializeResult TryLoadDocumentFromFile(string filePath) =>
        TryLoadDocumentFromFile(filePath, CancellationToken.None);

    public DeserializeResult TryLoadDocumentFromFile(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
        {
            return DeserializeResult.Fail(new ParseError { Message = $"File not found: {filePath}" });
        }

        try
        {
            var json = AtomicDocumentFile.ReadAllBytesBounded(
                filePath,
                FileDocumentStore.DefaultMaxDocumentBytes,
                cancellationToken);
            return DocumentSerializer.TryDeserialize(json);
        }
        catch (IOException ex)
        {
            return DeserializeResult.Fail(new ParseError { Message = $"Failed to read file: {ex.Message}" });
        }
        catch (DocumentEngineException ex) when (ex.Code == EngineErrors.ResourceLimit)
        {
            return DeserializeResult.Fail(new ParseError { Message = ex.Message });
        }
    }

    public DocumentHandle? Open(string idOrPath) => Open(idOrPath, CancellationToken.None);

    public DocumentHandle? Open(string idOrPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Guid.TryParse(idOrPath, out var id) && _store is not null)
        {
            var fromStore = _store.Load(id, cancellationToken);
            return fromStore is null
                ? null
                : new DocumentHandle(fromStore.Document, fromStore.SourcePath) { IsStoreSource = true };
        }

        if (File.Exists(idOrPath))
        {
            var fromFile = LoadDocumentFromFile(idOrPath, cancellationToken);
            return fromFile is null ? null : new DocumentHandle(fromFile, Path.GetFullPath(idOrPath));
        }

        return null;
    }

    public void SaveDocument(Document document, long? expectedRevn = null) =>
        SaveDocument(document, expectedRevn, CancellationToken.None);

    public void SaveDocument(Document document, long? expectedRevn, CancellationToken cancellationToken)
    {
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureSupportedVersion(document);
            RequireStore().Save(new DocumentStoreEntry(document, SourcePath: null), expectedRevn, cancellationToken);
        }
    }

    public void CreateDocumentInStore(Document document) =>
        CreateDocumentInStore(document, CancellationToken.None);

    public void CreateDocumentInStore(Document document, CancellationToken cancellationToken)
    {
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureSupportedVersion(document);
            RequireStore().Create(document, cancellationToken);
        }
    }

    public void SaveDocumentToFile(Document document, string filePath, long? expectedRevn = null) =>
        SaveDocumentToFile(document, filePath, expectedRevn, CancellationToken.None);

    public void SaveDocumentToFile(
        Document document,
        string filePath,
        long? expectedRevn,
        CancellationToken cancellationToken)
    {
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureSupportedVersion(document);
            var fullPath = Path.GetFullPath(filePath);
            AtomicDocumentFile.Save(fullPath, document, expectedRevn, cancellationToken: cancellationToken);
        }
    }

    public void CreateDocumentFile(Document document, string filePath) =>
        CreateDocumentFile(document, filePath, CancellationToken.None);

    public void CreateDocumentFile(Document document, string filePath, CancellationToken cancellationToken)
    {
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureSupportedVersion(document);
            AtomicDocumentFile.Create(Path.GetFullPath(filePath), document, cancellationToken: cancellationToken);
        }
    }

    public void Save(DocumentHandle handle, long? expectedRevn = null) =>
        Save(handle, expectedRevn, CancellationToken.None);

    public void Save(DocumentHandle handle, long? expectedRevn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        lock (DocumentSynchronization.For(handle.Document))
        {
            DocumentSynchronization.EnsureSupportedVersion(handle.Document);
            if (handle.IsStoreSource)
            {
                RequireStore().Save(
                    new DocumentStoreEntry(handle.Document, handle.SourcePath),
                    expectedRevn,
                    cancellationToken);
            }
            else if (handle.SourcePath is not null)
            {
                SaveDocumentToFile(handle.Document, handle.SourcePath, expectedRevn, cancellationToken);
            }
            else
            {
                SaveDocument(handle.Document, expectedRevn, cancellationToken);
            }
        }
    }

    public void DeleteDocument(Guid id) => DeleteDocument(id, CancellationToken.None);

    public void DeleteDocument(Guid id, CancellationToken cancellationToken) =>
        RequireStore().Delete(id, cancellationToken);

    public IEnumerable<DocumentInfo> ListDocuments() => ListDocuments(CancellationToken.None);

    public IEnumerable<DocumentInfo> ListDocuments(CancellationToken cancellationToken) =>
        RequireStore().List(cancellationToken);

    // ---- Query ----

    public ValidationResult Validate(Document document)
    {
        return Validate(document, ValidationOptions.Default);
    }

    public ValidationResult Validate(Document document, ValidationOptions options)
    {
        lock (DocumentSynchronization.For(document))
            return DocumentValidator.Validate(document, options);
    }

    public int CountNodes(Document document)
    {
        lock (DocumentSynchronization.For(document))
            return DocumentIndex.Build(document).Count;
    }

    public Node? FindNode(Document document, Guid nodeId)
    {
        lock (DocumentSynchronization.For(document))
            return DocumentIndex.Build(document).Find(nodeId);
    }

    public IReadOnlyList<NodeInfo> ListNodes(Document document)
    {
        lock (DocumentSynchronization.For(document))
        {
            var list = new List<NodeInfo>();
            Flatten(document.Root, "root", 0, list);
            return list;
        }
    }

    public string ToJson(Document document)
    {
        lock (DocumentSynchronization.For(document))
            return DocumentSerializer.Serialize(document);
    }

    public NodePage ListNodesPage(Document document, NodePageQuery query, CancellationToken cancellationToken = default)
    {
        lock (DocumentSynchronization.For(document))
            return NodePageTraversal.Query(document, query, cancellationToken);
    }

    /// <summary>Lowercase type name for a node ("rectangle", "frame", ...).</summary>
    public static string GetNodeTypeName(Node node) =>
        node.GetType().Name.Replace("Node", "", StringComparison.Ordinal).ToLowerInvariant();

    // ---- Mutation ----

    public Node AddNode(Document document, Guid? parentId, NewNodeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(spec);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            var (nodeType, points) = ValidateNewNodeSpec(document, spec);

            var index = DocumentIndex.Build(document);

            Node parentNode;
            if (parentId is null || parentId == document.Root.Id)
            {
                parentNode = document.Root;
            }
            else
            {
                parentNode = index.Find(parentId.Value)
                    ?? throw new DocumentEngineException(EngineErrors.ParentNotFound,
                        $"Parent node not found: {parentId}");
            }

            if (parentNode is not IContainerNode container)
            {
                throw new DocumentEngineException(EngineErrors.ParentNotContainer,
                    $"Parent node {parentNode.Id} ({GetNodeTypeName(parentNode)}) cannot contain children");
            }

            var node = CreateNodeFromSpec(spec, nodeType, points);

            var lastOrder = container.Children.Count == 0
                ? null
                : container.Children.Select(c => c.Order).Max(StringComparer.Ordinal);
            try
            {
                node.Order = FractionalIndex.Between(lastOrder, null);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Cannot assign a valid sibling order: {ex.Message}");
            }

            if (container.Children.Any(child => child.Order == node.Order))
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Generated duplicate sibling order '{node.Order}'");
            }

            container.Children.Add(node);
            DocumentSynchronization.IncrementRevision(document);

            return node;
        }
    }

    public Node UpdateNode(Document document, Guid nodeId, NodeUpdates updates)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(updates);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);

            var node = DocumentIndex.Build(document).Find(nodeId)
            ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                $"Node not found: {nodeId}");

            ValidateNodeUpdates(document, node, updates);

            if (updates.Name is not null)
            {
                node.Name = updates.Name;
            }

            if (updates.Visible.HasValue)
            {
                node.Visible = updates.Visible.Value;
            }

            if (updates.X.HasValue || updates.Y.HasValue)
            {
                node.Transform ??= new Transform();
                if (updates.X.HasValue)
                {
                    node.Transform.X = updates.X.Value;
                }

                if (updates.Y.HasValue)
                {
                    node.Transform.Y = updates.Y.Value;
                }
            }

            ApplyStyleUpdates(node, updates);

            if (updates.Width.HasValue || updates.Height.HasValue)
            {
                ApplySize(node, updates.Width, updates.Height);
            }

            ApplyTypeSpecificUpdates(node, updates);

            DocumentSynchronization.IncrementRevision(document);

            return node;
        }
    }

    public Node MoveNode(Document document, Guid nodeId, Guid? newParentId)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            if (document.Root.Id == nodeId)
            {
                throw new DocumentEngineException(EngineErrors.CannotDeleteRoot,
                    "Cannot move the root node");
            }

            var index = DocumentIndex.Build(document);
            var node = index.Find(nodeId)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                    $"Node not found: {nodeId}");

            var oldParent = index.ParentOf(nodeId)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                    $"Node {nodeId} has no parent link");

            Node newParentNode;
            if (newParentId is null || newParentId == document.Root.Id)
            {
                newParentNode = document.Root;
            }
            else
            {
                newParentNode = index.Find(newParentId.Value)
                    ?? throw new DocumentEngineException(EngineErrors.ParentNotFound,
                        $"Parent node not found: {newParentId}");
            }

            if (newParentNode is not IContainerNode newContainer)
            {
                throw new DocumentEngineException(EngineErrors.ParentNotContainer,
                    $"Target parent {newParentNode.Id} ({GetNodeTypeName(newParentNode)}) cannot contain children");
            }

            if (IsDescendantOf(newParentNode, nodeId, index))
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    "Cannot move a node into its own descendant");
            }

            var lastOrder = newContainer.Children.Count == 0
                ? null
                : newContainer.Children.Select(c => c.Order).Max(StringComparer.Ordinal);
            string newOrder;
            try
            {
                newOrder = FractionalIndex.Between(lastOrder, null);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Cannot assign a valid sibling order: {ex.Message}");
            }

            if (newContainer.Children.Any(child => child.Id != nodeId && child.Order == newOrder))
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Moving the node would create duplicate sibling order '{newOrder}'");
            }

            oldParent.Children.Remove(node);
            node.Order = newOrder;

            newContainer.Children.Add(node);
            DocumentSynchronization.IncrementRevision(document);

            return node;
        }
    }

    public Node ReorderNode(Document document, Guid nodeId, Guid? beforeNodeId, Guid? afterNodeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            if (document.Root.Id == nodeId)
            {
                throw new DocumentEngineException(EngineErrors.CannotDeleteRoot,
                    "Cannot reorder the root node");
            }

            if (!beforeNodeId.HasValue && !afterNodeId.HasValue)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    "Reorder requires at least one sibling anchor");
            }

            if (beforeNodeId == nodeId || afterNodeId == nodeId)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    "A node cannot be its own reorder anchor");
            }

            if (beforeNodeId.HasValue && beforeNodeId == afterNodeId)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    "Before and after anchors must be different nodes");
            }

            var index = DocumentIndex.Build(document);
            var node = index.Find(nodeId)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                    $"Node not found: {nodeId}");

            var parent = index.ParentOf(nodeId)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                    $"Node {nodeId} has no parent link");

            string? beforeOrder = null;
            string? afterOrder = null;

            if (beforeNodeId.HasValue)
            {
                var beforeNode = parent.Children.FirstOrDefault(c => c.Id == beforeNodeId.Value);
                if (beforeNode is null)
                {
                    throw new DocumentEngineException(EngineErrors.NodeNotFound,
                        $"Before node not found in same parent: {beforeNodeId}");
                }
                beforeOrder = beforeNode.Order;
            }

            if (afterNodeId.HasValue)
            {
                var afterNode = parent.Children.FirstOrDefault(c => c.Id == afterNodeId.Value);
                if (afterNode is null)
                {
                    throw new DocumentEngineException(EngineErrors.NodeNotFound,
                        $"After node not found in same parent: {afterNodeId}");
                }
                afterOrder = afterNode.Order;
            }

            if (beforeOrder is not null && afterOrder is null)
            {
                afterOrder = parent.Children
                    .Where(child => child.Id != nodeId &&
                        string.Compare(child.Order, beforeOrder, StringComparison.Ordinal) > 0)
                    .Select(child => child.Order)
                    .Min(StringComparer.Ordinal);
            }
            else if (afterOrder is not null && beforeOrder is null)
            {
                beforeOrder = parent.Children
                    .Where(child => child.Id != nodeId &&
                        string.Compare(child.Order, afterOrder, StringComparison.Ordinal) < 0)
                    .Select(child => child.Order)
                    .Max(StringComparer.Ordinal);
            }

            if (beforeOrder is not null && afterOrder is not null &&
                string.Compare(beforeOrder, afterOrder, StringComparison.Ordinal) >= 0)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Before anchor order '{beforeOrder}' must be less than after anchor order '{afterOrder}'");
            }

            string newOrder;
            try
            {
                newOrder = FractionalIndex.Between(beforeOrder, afterOrder);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"No fractional order is available between the supplied anchors: {ex.Message}");
            }

            if (parent.Children.Any(child => child.Id != nodeId && child.Order == newOrder))
            {
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Reordering the node would create duplicate sibling order '{newOrder}'");
            }

            node.Order = newOrder;
            DocumentSynchronization.IncrementRevision(document);

            return node;
        }
    }

    private static bool IsDescendantOf(Node potentialDescendant, Guid ancestorId, DocumentIndex index)
    {
        var current = potentialDescendant;
        while (current is not null)
        {
            if (current.Id == ancestorId)
            {
                return true;
            }
            current = index.ParentOf(current.Id) as Node;
        }
        return false;
    }

    public void RemoveNode(Document document, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            if (document.Root.Id == nodeId)
            {
                throw new DocumentEngineException(EngineErrors.CannotDeleteRoot,
                    "Cannot delete the root node");
            }

            var index = DocumentIndex.Build(document);
            var node = index.Find(nodeId)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                    $"Node not found: {nodeId}");

            var parent = index.ParentOf(nodeId)
                ?? throw new DocumentEngineException(EngineErrors.NodeNotFound,
                    $"Node {nodeId} has no parent link");

            parent.Children.Remove(node);
            DocumentSynchronization.IncrementRevision(document);
        }
    }

    // ---- Patch (atomic multi-operation mutation) ----

    public PatchResult ApplyPatch(DocumentHandle handle, IReadOnlyList<PatchOperation> operations, long? expectedRevn = null) =>
        ApplyPatch(handle, operations, expectedRevn, CancellationToken.None);

    public PatchResult ApplyPatch(
        DocumentHandle handle,
        IReadOnlyList<PatchOperation> operations,
        long? expectedRevn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(operations);
        var document = handle.Document;
        lock (DocumentSynchronization.For(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var revn = document.Revn;
            if (operations.Count == 0)
            {
                return PatchFailure(revn, EngineErrors.InvalidArguments, "Patch contains no operations");
            }

            if (operations.Count > PatchLimits.MaxOperations)
            {
                return PatchFailure(revn, EngineErrors.ResourceLimit,
                    $"Patch contains {operations.Count} operations; the maximum is {PatchLimits.MaxOperations}");
            }

            if (expectedRevn is < 0)
            {
                return PatchFailure(revn, EngineErrors.InvalidProperty, "Expected revision cannot be negative");
            }

            try
            {
                DocumentSynchronization.EnsureCanMutate(document);
            }
            catch (DocumentEngineException ex)
            {
                return PatchFailure(revn, ex.Code, ex.Message);
            }

            var baseline = DocumentValidator.Validate(document).Errors
                .Select(ValidationErrorKey)
                .ToHashSet(StringComparer.Ordinal);

            Document working;
            try
            {
                working = CloneDocument(document);
            }
            catch (JsonException ex)
            {
                return PatchFailure(revn, EngineErrors.InvalidProperty, $"Document cannot be serialized: {ex.Message}");
            }

            var createdIds = new Dictionary<int, Guid>();
            var failure = ApplyPatchOperations(working, operations, revn, createdIds, cancellationToken);
            if (failure is not null)
            {
                return PatchResult.Fail(failure, revn);
            }

            var validation = DocumentValidator.Validate(working);
            if (validation.Errors.Count > 0)
            {
                var introduced = validation.Errors.FirstOrDefault(error => !baseline.Contains(ValidationErrorKey(error)));
                if (introduced is not null)
                {
                    var (index, error) = AttributeValidationError(document, operations, revn, baseline, cancellationToken)
                        ?? (null, introduced);
                    return PatchResult.Fail(
                        new PatchError(error.Code, DescribeOperation(index, operations, error.Message), index, error.Path),
                        revn);
                }

                var preexisting = validation.Errors[0];
                return PatchResult.Fail(
                    new PatchError(preexisting.Code,
                        $"Document fails validation before this patch and the patch does not resolve it: {preexisting.Message}",
                        null,
                        preexisting.Path),
                    revn);
            }

            working.Revn = checked(revn + 1);
            try
            {
                Save(handle with { Document = working }, expectedRevn, cancellationToken);
            }
            catch (DocumentEngineException ex)
            {
                return PatchFailure(revn, ex.Code, ex.Message);
            }

            CopyDocumentState(document, working);
            return PatchResult.Ok(document.Revn, operations.Count, createdIds, validation.Warnings);
        }
    }

    private PatchError? ApplyPatchOperations(
        Document working,
        IReadOnlyList<PatchOperation> operations,
        long revn,
        Dictionary<int, Guid> createdIds,
        CancellationToken cancellationToken)
    {
        var patcher = new DocumentPatcher(this, working);
        for (var index = 0; index < operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = operations[index]
                ?? throw new ArgumentException($"Operation {index} is null", nameof(operations));
            try
            {
                var created = patcher.Apply(operation);
                DocumentIndex.Build(working);
                working.Revn = revn;
                if (created is Guid id)
                {
                    createdIds[index] = id;
                }
            }
            catch (DocumentEngineException ex)
            {
                return new PatchError(ex.Code, DescribeOperation(index, operations, ex.Message), index);
            }
            catch (JsonException ex)
            {
                return new PatchError(EngineErrors.InvalidProperty,
                    DescribeOperation(index, operations, $"Node JSON is invalid: {ex.Message}"), index);
            }
        }

        return null;
    }

    /// <summary>
    /// Replay the patch on a fresh copy to find the first operation after which a validation
    /// error that was not present before the patch appears.
    /// </summary>
    private (int? Index, ValidationError Error)? AttributeValidationError(
        Document original,
        IReadOnlyList<PatchOperation> operations,
        long revn,
        HashSet<string> baseline,
        CancellationToken cancellationToken)
    {
        var probe = CloneDocument(original);
        var patcher = new DocumentPatcher(this, probe);
        for (var index = 0; index < operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                patcher.Apply(operations[index]);
                probe.Revn = revn;
            }
            catch (Exception ex) when (ex is DocumentEngineException or JsonException)
            {
                return null;
            }

            var error = DocumentValidator.Validate(probe).Errors
                .FirstOrDefault(candidate => !baseline.Contains(ValidationErrorKey(candidate)));
            if (error is not null)
            {
                return (index, error);
            }
        }

        return null;
    }

    private static string DescribeOperation(int? index, IReadOnlyList<PatchOperation> operations, string reason) =>
        index is int i ? $"Operation {i} ({operations[i].Op}): {reason}" : reason;

    private static string ValidationErrorKey(ValidationError error) => $"{error.Code}|{error.Path}";

    private static PatchResult PatchFailure(long revn, string code, string message) =>
        PatchResult.Fail(new PatchError(code, message), revn);

    private static Document CloneDocument(Document document) =>
        DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))
            ?? throw new JsonException("Document clone deserialized to null");

    private static void CopyDocumentState(Document target, Document source)
    {
        target.Schema = source.Schema;
        target.Version = source.Version;
        target.Id = source.Id;
        target.Name = source.Name;
        target.Revn = source.Revn;
        target.Canvas = source.Canvas;
        target.Root = source.Root;
        target.Tokens = source.Tokens;
        target.ExtensionData = source.ExtensionData;
    }

    // ---- Export / render ----

    public async Task<ExportResult> ExportAsync(Document document, ExportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!_exportAdapters.TryGetValue(options.Format, out var adapter))
        {
            throw new DocumentEngineException(EngineErrors.ExportUnsupported,
                $"Export format '{options.Format}' is not supported. Supported: {string.Join(", ", _exportAdapters.Keys)}");
        }

        Document snapshot;
        lock (DocumentSynchronization.For(document))
        {
            ct.ThrowIfCancellationRequested();
            snapshot = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))!;
        }
        return await adapter.ExportAsync(snapshot, options, ct);
    }

    public RenderResult Render(Document document, RenderOptions options) =>
        Render(document, options, CancellationToken.None);

    public RenderResult Render(Document document, RenderOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_renderer is null)
        {
            throw new DocumentEngineException(EngineErrors.RenderUnavailable,
                "No renderer is registered with the engine");
        }

        lock (DocumentSynchronization.For(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _renderer.Render(document, options, cancellationToken);
        }
    }

    // ---- Sub-node export (D-10) ----

    public Task<ExportResult> ExportNodeAsync(Document document, Guid nodeId, ExportOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return ExportAsync(BuildSubtreeDocument(document, nodeId, ct), options, ct);
    }

    public RenderResult RenderNode(Document document, Guid nodeId, RenderOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        return Render(BuildSubtreeDocument(document, nodeId, cancellationToken), options, cancellationToken);
    }

    /// <summary>
    /// Builds a transient document holding only the subtree rooted at <paramref name="nodeId"/>,
    /// cropped to that subtree's own bounds and shifted so its top-left maps to (0,0). The node
    /// is placed under a fresh, non-clipping root, so neither ancestor transforms nor ancestor
    /// clipping affect the exported asset — a sub-node export reflects the node in isolation
    /// (D-10). Document design tokens are carried over so token references resolve. Throws <see cref="EngineErrors.NodeNotFound"/> when the id is absent and
    /// <see cref="EngineErrors.NodeNoBounds"/> when the subtree has no visible extent.
    /// </summary>
    private Document BuildSubtreeDocument(Document document, Guid nodeId, CancellationToken cancellationToken)
    {
        Document copy;
        lock (DocumentSynchronization.For(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            copy = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(document))!;
        }

        var node = DocumentIndex.Build(copy).Find(nodeId)
            ?? throw new DocumentEngineException(EngineErrors.NodeNotFound, $"Node not found: {nodeId}");

        // Measure the subtree's world box under a fresh, non-clipping root so neither ancestor
        // transforms nor ancestor clipping influence the reported bounds.
        var probe = new Document
        {
            Id = Guid.NewGuid(),
            Name = copy.Name,
            Canvas = new Canvas { Width = copy.Canvas.Width, Height = copy.Canvas.Height },
            Tokens = copy.Tokens,
            Root = new FrameNode { ClipContent = false, Width = copy.Canvas.Width, Height = copy.Canvas.Height, Children = { node } }
        };
        var world = GetBounds(probe, node.Id, cancellationToken).FirstOrDefault(b => b.Id == node.Id)?.World;
        if (world is not { } box || box.Width <= 0 || box.Height <= 0)
        {
            throw new DocumentEngineException(EngineErrors.NodeNoBounds,
                $"Node {nodeId} has no visible bounds to export");
        }

        // Wrap the node in a translating group under a non-clipping root sized to the box, so the
        // subtree's top-left lands at the origin and the whole subtree sits inside the viewport.
        return new Document
        {
            Id = Guid.NewGuid(),
            Name = copy.Name,
            Canvas = new Canvas { Width = box.Width, Height = box.Height },
            // Design tokens are document-wide: token-referenced paint and typography in the
            // subtree must resolve exactly as they do in the whole document.
            Tokens = copy.Tokens,
            Root = new FrameNode
            {
                ClipContent = false,
                Width = box.Width,
                Height = box.Height,
                Children =
                {
                    new GroupNode
                    {
                        Transform = new Transform { X = -box.X, Y = -box.Y },
                        Children = { node }
                    }
                }
            }
        };
    }

    // ---- Internals ----

    private IDocumentStore RequireStore() =>
        _store ?? throw new DocumentEngineException(EngineErrors.StoreUnavailable,
            "This engine has no document store; use file-based operations instead");

    private static void Flatten(Node node, string path, int depth, List<NodeInfo> list)
    {
        list.Add(new NodeInfo(node.Id, GetNodeTypeName(node), node.Name, node.Order, path, node.Visible, depth));

        if (node is IContainerNode container)
        {
            var ordered = container.Children.OrderBy(c => c.Order, StringComparer.Ordinal).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                Flatten(ordered[i], $"{path}.children[{i}]", depth + 1, list);
            }
        }
    }

    private static Node CreateNodeFromSpec(NewNodeSpec spec, NodeType nodeType, List<Point> points)
    {
        Node node = nodeType switch
        {
            NodeType.Rectangle => new RectangleNode
            {
                Width = spec.Width ?? 100,
                Height = spec.Height ?? 100,
                CornerRadius = spec.CornerRadius ?? 0
            },
            NodeType.Ellipse => new EllipseNode
            {
                Width = spec.Width ?? 100,
                Height = spec.Height ?? 100
            },
            NodeType.Text => new TextNode
            {
                Text = spec.Text ?? "",
                FontFamily = spec.FontFamily,
                FontSize = spec.FontSize ?? 16,
                FontWeight = spec.FontWeight ?? 400,
                TypographyTokenRef = spec.TypographyTokenRef,
                TextAlign = NormalizeAlignment(spec.TextAlign),
                VerticalAlign = NormalizeAlignment(spec.VerticalAlign),
                MaxWidth = spec.MaxWidth,
                LineHeight = spec.LineHeight,
                LetterSpacing = spec.LetterSpacing
            },
            NodeType.Path => new PathNode
            {
                D = spec.PathData ?? ""
            },
            NodeType.Group => new GroupNode(),
            NodeType.Frame => new FrameNode
            {
                Width = spec.Width ?? 100,
                Height = spec.Height ?? 100
            },
            NodeType.Line => new LineNode
            {
                X1 = spec.X1 ?? 0,
                Y1 = spec.Y1 ?? 0,
                X2 = spec.X2 ?? 100,
                Y2 = spec.Y2 ?? 100
            },
            NodeType.Polygon => new PolygonNode
            {
                Points = points,
                Closed = spec.PolygonClosed ?? true
            },
            NodeType.Image => new ImageNode
            {
                Src = spec.ImageSrc ?? "",
                Width = spec.Width ?? 100,
                Height = spec.Height ?? 100
            },
            _ => throw new DocumentEngineException(EngineErrors.InvalidNodeType,
                $"Unknown node type: '{spec.Type}'")
        };

        node.Id = Guid.NewGuid();
        node.Name = spec.Name;

        if (spec.X != 0 || spec.Y != 0)
        {
            node.Transform = new Transform { X = spec.X, Y = spec.Y };
        }

        if (spec.Fill is not null || spec.Stroke is not null ||
            spec.FillTokenRef is not null || spec.StrokeTokenRef is not null || spec.Opacity.HasValue ||
            spec.FillRule is not null)
        {
            node.Style = new Style();
            if (spec.Fill is not null)
            {
                node.Style.Fill = new Fill { Color = spec.Fill };
            }
            else if (spec.FillTokenRef is not null)
            {
                node.Style.Fill = new Fill { TokenRef = spec.FillTokenRef };
            }

            if (spec.Stroke is not null)
            {
                node.Style.Stroke = new Stroke { Color = spec.Stroke };
            }
            else if (spec.StrokeTokenRef is not null)
            {
                node.Style.Stroke = new Stroke { TokenRef = spec.StrokeTokenRef };
            }

            if (spec.Opacity.HasValue)
            {
                node.Style.Opacity = spec.Opacity.Value;
            }

            if (spec.FillRule is not null)
            {
                node.Style.FillRule = NormalizeFillRule(spec.FillRule);
            }
        }

        return node;
    }

    private static void ApplySize(Node node, double? width, double? height)
    {
        switch (node)
        {
            case RectangleNode rect:
                if (width.HasValue)
                {
                    rect.Width = width.Value;
                }

                if (height.HasValue)
                {
                    rect.Height = height.Value;
                }

                break;

            case EllipseNode ellipse:
                if (width.HasValue)
                {
                    ellipse.Width = width.Value;
                }

                if (height.HasValue)
                {
                    ellipse.Height = height.Value;
                }

                break;

            case FrameNode frame:
                if (width.HasValue)
                {
                    frame.Width = width.Value;
                }

                if (height.HasValue)
                {
                    frame.Height = height.Value;
                }

                break;

            case ImageNode image:
                if (width.HasValue)
                {
                    image.Width = width.Value;
                }

                if (height.HasValue)
                {
                    image.Height = height.Value;
                }

                break;

            default:
                throw new DocumentEngineException(EngineErrors.InvalidProperty,
                    $"Node type '{GetNodeTypeName(node)}' has no width/height");
        }
    }

    private static void ApplyStyleUpdates(Node node, NodeUpdates updates)
    {
        if (updates.Fill is not null)
        {
            if (PropertyClear.IsClear(updates.Fill))
            {
                if (node.Style is not null)
                {
                    node.Style.Fill = null;
                }
            }
            else
            {
                node.Style ??= new Style();
                node.Style.Fill = new Fill { Color = updates.Fill };
            }
        }
        else if (updates.FillTokenRef is not null)
        {
            node.Style ??= new Style();
            node.Style.Fill = new Fill { TokenRef = updates.FillTokenRef };
        }

        if (updates.Stroke is not null)
        {
            if (PropertyClear.IsClear(updates.Stroke))
            {
                if (node.Style is not null)
                {
                    node.Style.Stroke = null;
                }
            }
            else
            {
                node.Style ??= new Style();
                node.Style.Stroke = new Stroke { Color = updates.Stroke };
            }
        }
        else if (updates.StrokeTokenRef is not null)
        {
            node.Style ??= new Style();
            node.Style.Stroke = new Stroke { TokenRef = updates.StrokeTokenRef };
        }

        if (updates.Opacity.HasValue)
        {
            node.Style ??= new Style();
            node.Style.Opacity = updates.Opacity.Value;
        }

        if (updates.FillRule is not null)
        {
            if (PropertyClear.IsClear(updates.FillRule))
            {
                if (node.Style is not null)
                {
                    node.Style.FillRule = null;
                }
            }
            else
            {
                node.Style ??= new Style();
                node.Style.FillRule = NormalizeFillRule(updates.FillRule);
            }
        }
    }

    private static void ApplyTypeSpecificUpdates(Node node, NodeUpdates updates)
    {
        switch (node)
        {
            case TextNode textNode:
                if (updates.Text is not null)
                {
                    textNode.Text = updates.Text;
                }
                if (updates.FontFamily is not null)
                {
                    textNode.FontFamily = updates.FontFamily;
                }
                if (updates.FontSize.HasValue)
                {
                    textNode.FontSize = updates.FontSize.Value;
                }
                if (updates.FontWeight.HasValue)
                {
                    textNode.FontWeight = updates.FontWeight.Value;
                }
                if (updates.TypographyTokenRef is not null)
                {
                    textNode.TypographyTokenRef = PropertyClear.IsClear(updates.TypographyTokenRef)
                        ? null
                        : updates.TypographyTokenRef;
                }
                if (updates.TextAlign is not null)
                {
                    textNode.TextAlign = PropertyClear.IsClear(updates.TextAlign) ? null : NormalizeAlignment(updates.TextAlign);
                }
                if (updates.VerticalAlign is not null)
                {
                    textNode.VerticalAlign = PropertyClear.IsClear(updates.VerticalAlign) ? null : NormalizeAlignment(updates.VerticalAlign);
                }
                if (updates.MaxWidth.HasValue)
                {
                    textNode.MaxWidth = updates.MaxWidth.Value;
                }
                if (updates.LineHeight.HasValue)
                {
                    textNode.LineHeight = updates.LineHeight.Value;
                }
                if (updates.LetterSpacing.HasValue)
                {
                    textNode.LetterSpacing = updates.LetterSpacing.Value;
                }
                break;

            case PathNode pathNode:
                if (updates.PathData is not null)
                {
                    pathNode.D = updates.PathData;
                }
                break;

            case RectangleNode rectNode:
                if (updates.CornerRadius.HasValue)
                {
                    rectNode.CornerRadius = updates.CornerRadius.Value;
                }
                break;
        }
    }

    private static (NodeType NodeType, List<Point> Points) ValidateNewNodeSpec(Document document, NewNodeSpec spec)
    {
        ValidateStringLength(spec.Type, DocumentLimits.MaxNameLength, "Node type");
        if (string.IsNullOrWhiteSpace(spec.Type) || !NodeTypeParser.TryParse(spec.Type, out var nodeType))
        {
            throw new DocumentEngineException(EngineErrors.InvalidNodeType,
                $"Unknown node type: '{spec.Type}'. Accepted: {string.Join(", ", NodeTypeParser.AcceptedTypes)}");
        }

        ValidateFinite(spec.X, "X coordinate");
        ValidateFinite(spec.Y, "Y coordinate");
        ValidateOptionalFinite(spec.Width, "width", EngineErrors.InvalidDimensions);
        ValidateOptionalFinite(spec.Height, "height", EngineErrors.InvalidDimensions);
        ValidateOptionalFinite(spec.CornerRadius, "corner radius");
        ValidateOptionalFinite(spec.FontSize, "font size");
        ValidateOptionalFinite(spec.MaxWidth, "max width");
        ValidateOptionalFinite(spec.LineHeight, "line height");
        ValidateOptionalFinite(spec.LetterSpacing, "letter spacing");
        ValidateOptionalFinite(spec.Opacity, "opacity");
        ValidateOptionalFinite(spec.X1, "line X1");
        ValidateOptionalFinite(spec.Y1, "line Y1");
        ValidateOptionalFinite(spec.X2, "line X2");
        ValidateOptionalFinite(spec.Y2, "line Y2");
        ValidateOptionalStringLength(spec.Name, DocumentLimits.MaxNameLength, "Node name");
        ValidateOptionalStringLength(spec.Text, DocumentLimits.MaxTextLength, "Text");
        ValidateOptionalStringLength(spec.PathData, DocumentLimits.MaxPathDataLength, "Path data");
        ValidateOptionalStringLength(spec.FontFamily, DocumentLimits.MaxFontFamilyLength, "Font family");
        ValidateOptionalStringLength(spec.FillTokenRef, DocumentLimits.MaxTokenReferenceLength, "Fill token reference");
        ValidateOptionalStringLength(spec.StrokeTokenRef, DocumentLimits.MaxTokenReferenceLength, "Stroke token reference");
        ValidateOptionalStringLength(spec.TypographyTokenRef, DocumentLimits.MaxTokenReferenceLength, "Typography token reference");
        ValidateOptionalStringLength(spec.ImageSrc, DocumentLimits.MaxImageSourceLength, "Image source");
        ValidateOptionalStringLength(spec.Points, DocumentLimits.MaxPathDataLength, "Polygon points");
        ValidateOptionalStringLength(spec.FillRule, DocumentLimits.MaxStyleStringLength, "Fill rule");

        if (spec.Width is < 0 || spec.Height is < 0)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Node dimensions cannot be negative; got {spec.Width}x{spec.Height}");
        }

        if (spec.CornerRadius is < 0)
        {
            throw InvalidProperty($"Corner radius cannot be negative; got {spec.CornerRadius}");
        }

        if (spec.FontSize is <= 0)
        {
            throw InvalidProperty($"Font size must be positive; got {spec.FontSize}");
        }

        if (spec.FontWeight is < 100 or > 900)
        {
            throw InvalidProperty($"Font weight must be between 100 and 900; got {spec.FontWeight}");
        }

        ValidateTextLayout(spec.TextAlign, spec.VerticalAlign, spec.MaxWidth, spec.LineHeight, allowClear: false);

        if (spec.Opacity is < 0 or > 1)
        {
            throw InvalidProperty($"Opacity must be between 0 and 1; got {spec.Opacity}");
        }

        ValidateColor(spec.Fill, "fill");
        ValidateColor(spec.Stroke, "stroke");
        ValidateTokenInput(document, spec.Fill, spec.FillTokenRef, "fill");
        ValidateTokenInput(document, spec.Stroke, spec.StrokeTokenRef, "stroke");
        ValidateTypographyTokenReference(document, spec.TypographyTokenRef);
        if (spec.FillRule is not null)
            NormalizeFillRule(spec.FillRule);

        bool hasTextProps = spec.Text is not null || spec.FontFamily is not null ||
            spec.FontSize.HasValue || spec.FontWeight.HasValue || spec.TypographyTokenRef is not null ||
            spec.TextAlign is not null || spec.VerticalAlign is not null ||
            spec.MaxWidth.HasValue || spec.LineHeight.HasValue || spec.LetterSpacing.HasValue;
        bool hasLineProps = spec.X1.HasValue || spec.Y1.HasValue || spec.X2.HasValue || spec.Y2.HasValue;
        bool hasPolygonProps = spec.Points is not null || spec.PolygonClosed.HasValue;
        bool hasDimensions = spec.Width.HasValue || spec.Height.HasValue;

        RequireNodeType(hasTextProps, nodeType == NodeType.Text, nodeType, "text properties");
        RequireNodeType(spec.PathData is not null, nodeType == NodeType.Path, nodeType, "path data");
        RequireNodeType(spec.CornerRadius.HasValue, nodeType == NodeType.Rectangle, nodeType, "corner radius");
        RequireNodeType(hasLineProps, nodeType == NodeType.Line, nodeType, "line coordinates");
        RequireNodeType(hasPolygonProps, nodeType == NodeType.Polygon, nodeType, "polygon properties");
        RequireNodeType(spec.ImageSrc is not null, nodeType == NodeType.Image, nodeType, "image source");
        RequireNodeType(spec.FillRule is not null, nodeType is NodeType.Path or NodeType.Polygon, nodeType, "a fill rule");
        RequireNodeType(
            hasDimensions,
            nodeType is NodeType.Rectangle or NodeType.Ellipse or NodeType.Frame or NodeType.Image,
            nodeType,
            "width/height");

        return (nodeType, nodeType == NodeType.Polygon ? ParsePoints(spec.Points) : []);
    }

    private static void ValidateNodeUpdates(Document document, Node node, NodeUpdates updates)
    {
        ValidateOptionalFinite(updates.Width, "width", EngineErrors.InvalidDimensions);
        ValidateOptionalFinite(updates.Height, "height", EngineErrors.InvalidDimensions);
        ValidateOptionalFinite(updates.X, "X coordinate");
        ValidateOptionalFinite(updates.Y, "Y coordinate");
        ValidateOptionalFinite(updates.CornerRadius, "corner radius");
        ValidateOptionalFinite(updates.FontSize, "font size");
        ValidateOptionalFinite(updates.MaxWidth, "max width");
        ValidateOptionalFinite(updates.LineHeight, "line height");
        ValidateOptionalFinite(updates.LetterSpacing, "letter spacing");
        ValidateOptionalFinite(updates.Opacity, "opacity");
        ValidateOptionalStringLength(updates.Name, DocumentLimits.MaxNameLength, "Node name");
        ValidateOptionalStringLength(updates.Text, DocumentLimits.MaxTextLength, "Text");
        ValidateOptionalStringLength(updates.PathData, DocumentLimits.MaxPathDataLength, "Path data");
        ValidateOptionalStringLength(updates.FontFamily, DocumentLimits.MaxFontFamilyLength, "Font family");
        ValidateOptionalStringLength(updates.FillTokenRef, DocumentLimits.MaxTokenReferenceLength, "Fill token reference");
        ValidateOptionalStringLength(updates.StrokeTokenRef, DocumentLimits.MaxTokenReferenceLength, "Stroke token reference");
        ValidateOptionalStringLength(updates.TypographyTokenRef, DocumentLimits.MaxTokenReferenceLength, "Typography token reference");
        ValidateOptionalStringLength(updates.FillRule, DocumentLimits.MaxStyleStringLength, "Fill rule");

        if (updates.Width is < 0 || updates.Height is < 0)
        {
            throw new DocumentEngineException(EngineErrors.InvalidDimensions,
                $"Node dimensions cannot be negative; got {updates.Width}x{updates.Height}");
        }

        if (updates.Opacity is < 0 or > 1)
        {
            throw InvalidProperty($"Opacity must be between 0 and 1; got {updates.Opacity}");
        }

        if (updates.FontWeight is < 100 or > 900)
        {
            throw InvalidProperty($"Font weight must be between 100 and 900; got {updates.FontWeight}");
        }

        if (updates.FontSize is <= 0)
        {
            throw InvalidProperty($"Font size must be positive; got {updates.FontSize}");
        }

        ValidateTextLayout(updates.TextAlign, updates.VerticalAlign, updates.MaxWidth, updates.LineHeight, allowClear: true);

        if (updates.CornerRadius is < 0)
        {
            throw InvalidProperty($"Corner radius cannot be negative; got {updates.CornerRadius}");
        }

        ValidateColor(updates.Fill, "fill", allowClear: true);
        ValidateColor(updates.Stroke, "stroke", allowClear: true);
        ValidateTokenInput(document, updates.Fill, updates.FillTokenRef, "fill");
        ValidateTokenInput(document, updates.Stroke, updates.StrokeTokenRef, "stroke");
        if (!PropertyClear.IsClear(updates.TypographyTokenRef))
            ValidateTypographyTokenReference(document, updates.TypographyTokenRef);
        if (updates.FillRule is not null && !PropertyClear.IsClear(updates.FillRule))
        {
            NormalizeFillRule(updates.FillRule);
            if (node is not (PathNode or PolygonNode))
                throw InvalidProperty($"Node type '{GetNodeTypeName(node)}' does not support a fill rule");
        }

        bool hasTextProps = updates.Text is not null || updates.FontFamily is not null ||
            updates.FontSize.HasValue || updates.FontWeight.HasValue || updates.TypographyTokenRef is not null ||
            updates.TextAlign is not null || updates.VerticalAlign is not null ||
            updates.MaxWidth.HasValue || updates.LineHeight.HasValue || updates.LetterSpacing.HasValue;
        bool hasSize = updates.Width.HasValue || updates.Height.HasValue;

        if (hasTextProps && node is not TextNode)
        {
            throw InvalidProperty($"Node type '{GetNodeTypeName(node)}' does not support text properties");
        }

        if (updates.PathData is not null && node is not PathNode)
        {
            throw InvalidProperty($"Node type '{GetNodeTypeName(node)}' does not support path data");
        }

        if (updates.CornerRadius.HasValue && node is not RectangleNode)
        {
            throw InvalidProperty($"Node type '{GetNodeTypeName(node)}' does not support corner radius");
        }

        if (hasSize && node is not (RectangleNode or EllipseNode or FrameNode or ImageNode))
        {
            throw InvalidProperty($"Node type '{GetNodeTypeName(node)}' has no width/height");
        }
    }

    private static List<Point> ParsePoints(string? pointsString)
    {
        if (string.IsNullOrWhiteSpace(pointsString))
        {
            return [];
        }

        var points = new List<Point>();
        var pairs = pointsString.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var coords = pair.Split(',');
            if (coords.Length != 2 ||
                !double.TryParse(coords[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(coords[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.IsFinite(x) || !double.IsFinite(y))
            {
                throw InvalidProperty($"Invalid polygon point '{pair}'; expected finite invariant x,y coordinates");
            }

            points.Add(new Point { X = x, Y = y });
        }

        return points;
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw InvalidProperty($"{name} must be finite; got {value}");
        }
    }

    private static void ValidateOptionalFinite(double? value, string name, string code = EngineErrors.InvalidProperty)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        {
            throw new DocumentEngineException(code, $"{name} must be finite; got {value}");
        }
    }

    private static string NormalizeFillRule(string fillRule)
    {
        if (!FillRules.TryNormalize(fillRule, out var normalized))
            throw InvalidProperty($"Fill rule must be {FillRules.NonZero} or {FillRules.EvenOdd}; got '{fillRule}'");
        return normalized;
    }

    private static void ValidateColor(string? color, string property, bool allowClear = false)
    {
        if (color is null || (allowClear && PropertyClear.IsClear(color)))
        {
            return;
        }

        if (!DocumentLimits.IsValidColor(color))
        {
            throw InvalidProperty($"Invalid {property} color: '{color}'");
        }
    }

    private static void ValidateTokenInput(
        Document document,
        string? literal,
        string? reference,
        string property)
    {
        if (literal is not null && reference is not null)
        {
            throw InvalidProperty($"Specify either {property} color or {property} token reference, not both");
        }
        if (reference is null)
        {
            return;
        }

        var tokenName = NormalizeTokenReference(reference);
        if (tokenName is null || document.Tokens?.Colors?.ContainsKey(tokenName) != true)
        {
            throw InvalidProperty($"Color token reference '{reference}' was not found");
        }
    }

    /// <summary>Alignment values are matched case-insensitively and stored in lower case.</summary>
    private static string? NormalizeAlignment(string? value) => value?.Trim().ToLowerInvariant();

    private static void ValidateTextLayout(string? textAlign, string? verticalAlign, double? maxWidth, double? lineHeight, bool allowClear)
    {
        if (textAlign is not null && !(allowClear && PropertyClear.IsClear(textAlign))
            && !TextLayout.IsValidTextAlign(NormalizeAlignment(textAlign)))
        {
            throw InvalidProperty($"Text align must be one of {string.Join(", ", TextLayout.TextAlignValues)}");
        }

        if (verticalAlign is not null && !(allowClear && PropertyClear.IsClear(verticalAlign))
            && !TextLayout.IsValidVerticalAlign(NormalizeAlignment(verticalAlign)))
        {
            throw InvalidProperty($"Vertical align must be one of {string.Join(", ", TextLayout.VerticalAlignValues)}");
        }

        if (maxWidth is <= 0)
        {
            throw InvalidProperty($"Max width must be positive; got {maxWidth}");
        }

        if (lineHeight is <= 0)
        {
            throw InvalidProperty($"Line height must be positive; got {lineHeight}");
        }
    }

    private static void ValidateTypographyTokenReference(Document document, string? reference)
    {
        if (reference is null)
        {
            return;
        }

        var tokenName = NormalizeTokenReference(reference);
        if (tokenName is null || document.Tokens?.Typography?.ContainsKey(tokenName) != true)
        {
            throw InvalidProperty($"Typography token reference '{reference}' was not found");
        }
    }

    private static string? NormalizeTokenReference(string reference)
    {
        if (DocumentLimits.TryGetTokenReference(reference, out var tokenName))
        {
            return tokenName;
        }
        return DocumentLimits.IsValidTokenName(reference) ? reference : null;
    }

    private static void ValidateStringLength(string value, int maximumLength, string property)
    {
        if (value.Length > maximumLength)
            throw InvalidProperty($"{property} exceeds maximum length ({maximumLength})");
    }

    private static void ValidateOptionalStringLength(string? value, int maximumLength, string property)
    {
        if (value is not null)
            ValidateStringLength(value, maximumLength, property);
    }

    private static void RequireNodeType(bool supplied, bool supported, NodeType actualType, string property)
    {
        if (supplied && !supported)
        {
            throw InvalidProperty($"Node type '{actualType.ToString().ToLowerInvariant()}' does not support {property}");
        }
    }

    private static DocumentEngineException InvalidProperty(string message) =>
        new(EngineErrors.InvalidProperty, message);

    // ---- Import ----

    private readonly SvgImportService _svgImport = new();

    public SvgImportResult ImportSvg(string svgContent, SvgImportOptions? options = null) =>
        ImportSvg(svgContent, options, CancellationToken.None);

    public SvgImportResult ImportSvg(string svgContent, CancellationToken cancellationToken) =>
        ImportSvg(svgContent, null, cancellationToken);

    public SvgImportResult ImportSvg(
        string svgContent,
        SvgImportOptions? options,
        CancellationToken cancellationToken) =>
        _svgImport.Import(svgContent, options, cancellationToken);

    public SvgImportResult ImportSvgFromFile(string filePath, SvgImportOptions? options = null) =>
        ImportSvgFromFile(filePath, options, CancellationToken.None);

    public SvgImportResult ImportSvgFromFile(string filePath, CancellationToken cancellationToken) =>
        ImportSvgFromFile(filePath, null, cancellationToken);

    public SvgImportResult ImportSvgFromFile(
        string filePath,
        SvgImportOptions? options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
        {
            return SvgImportResult.Fail("FILE_NOT_FOUND", $"File not found: {filePath}");
        }

        try
        {
            var svgBytes = AtomicDocumentFile.ReadAllBytesBounded(
                filePath,
                SvgImportService.MaxInputBytes,
                cancellationToken);
            var svgContent = System.Text.Encoding.UTF8.GetString(svgBytes);
            options ??= new SvgImportOptions();
            options = options with
            {
                DocumentName = options.DocumentName ?? Path.GetFileNameWithoutExtension(filePath)
            };
            return _svgImport.Import(svgContent, options, cancellationToken);
        }
        catch (IOException ex)
        {
            return SvgImportResult.Fail("FILE_READ_ERROR", $"Failed to read file: {ex.Message}");
        }
        catch (DocumentEngineException ex) when (ex.Code == EngineErrors.ResourceLimit)
        {
            return SvgImportResult.Fail("SVG_RESOURCE_LIMIT", ex.Message);
        }
    }

    // ---- Design tokens ----

    public void SetColorToken(Document document, string name, string color)
        => _tokenService.SetColorToken(document, name, color);

    public bool RemoveColorToken(Document document, string name)
        => _tokenService.RemoveColorToken(document, name);

    public void SetTypographyToken(Document document, string name, TypographyToken token)
        => _tokenService.SetTypographyToken(document, name, token);

    public bool RemoveTypographyToken(Document document, string name)
        => _tokenService.RemoveTypographyToken(document, name);

    public IReadOnlyDictionary<string, string> GetColorTokens(Document document)
        => _tokenService.GetColorTokens(document);

    public IReadOnlyDictionary<string, TypographyToken> GetTypographyTokens(Document document)
        => _tokenService.GetTypographyTokens(document);

    public PaletteApplyResult ApplyPalette(Document document, string paletteName)
        => _tokenService.ApplyPalette(document, paletteName);

    public IReadOnlyList<BundledPaletteInfo> ListPalettes()
        => _tokenService.ListPalettes();

    public TokenImportResult ImportDtcgTokens(Document document, string json)
        => _tokenService.ImportDtcg(document, json);

    public TokenImportResult ImportDtcgTokens(
        Document document,
        string json,
        CancellationToken cancellationToken)
        => _tokenService.ImportDtcg(document, json, cancellationToken);

    public TokenExportResult ExportDtcgTokens(Document document, bool resolveReferences = false)
        => _tokenService.ExportDtcg(document, resolveReferences);

    public string? ResolveColorRef(Document document, string? reference)
        => _tokenService.ResolveColorRef(document, reference);

    public Document ResolveAllTokenRefs(Document document)
        => _tokenService.ResolveAllTokenRefs(document);

    // ---- Path operations (Clipper2) ----

    public PathOperationResult PathBooleanOp(string subjectPath, string clipPath, BooleanOperation operation)
        => PathBooleanOp(subjectPath, clipPath, operation, CancellationToken.None);

    public PathOperationResult PathBooleanOp(
        string subjectPath,
        string clipPath,
        BooleanOperation operation,
        CancellationToken cancellationToken)
        => _pathOps.BooleanOp(subjectPath, clipPath, operation, cancellationToken);

    public PathOperationResult PathBooleanOp(
        string subjectPath,
        string clipPath,
        BooleanOperation operation,
        PathFillRule fillRule,
        CancellationToken cancellationToken)
        => _pathOps.BooleanOp(subjectPath, clipPath, operation, fillRule, cancellationToken);

    public PathOperationResult PathStrokeOffset(string path, double offset, OffsetJoinType joinType = OffsetJoinType.Round, OffsetEndType endType = OffsetEndType.Polygon, double miterLimit = 2.0)
        => PathStrokeOffset(path, offset, joinType, endType, miterLimit, CancellationToken.None);

    public PathOperationResult PathStrokeOffset(
        string path,
        double offset,
        OffsetJoinType joinType,
        OffsetEndType endType,
        double miterLimit,
        CancellationToken cancellationToken)
        => _pathOps.StrokeOffset(path, offset, joinType, endType, miterLimit, cancellationToken);

    public PathOperationResult PathStrokeOffset(
        string path,
        double offset,
        OffsetJoinType joinType,
        OffsetEndType endType,
        double miterLimit,
        PathFillRule fillRule,
        CancellationToken cancellationToken)
        => _pathOps.StrokeOffset(path, offset, joinType, endType, miterLimit, fillRule, cancellationToken);

    public PathOperationResult PathUnionAll(IEnumerable<string> paths)
        => PathUnionAll(paths, CancellationToken.None);

    public PathOperationResult PathUnionAll(IEnumerable<string> paths, CancellationToken cancellationToken)
        => _pathOps.UnionAll(paths, cancellationToken);

    public PathOperationResult PathUnionAll(IEnumerable<string> paths, PathFillRule fillRule, CancellationToken cancellationToken)
        => _pathOps.UnionAll(paths, fillRule, cancellationToken);

    // ---- Layout: bounds and alignment helpers ----

    public IReadOnlyList<NodeBounds> GetBounds(Document document, Guid? nodeId = null) =>
        GetBounds(document, nodeId, CancellationToken.None);

    public IReadOnlyList<NodeBounds> GetBounds(Document document, Guid? nodeId, CancellationToken cancellationToken) =>
        GetBounds(document, nodeId, null, cancellationToken);

    public IReadOnlyList<NodeBounds> GetBounds(Document document, Guid? nodeId, FontPolicy? fontPolicy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (DocumentSynchronization.For(document))
        {
            var calculator = CreateBoundsCalculator(document, fontPolicy, cancellationToken, out var rented);
            try { return calculator.Compute(nodeId); }
            finally { rented?.Dispose(); }
        }
    }

    public AlignResult AlignNodes(Document document, AlignRequest request) =>
        AlignNodes(document, request, CancellationToken.None);

    public AlignResult AlignNodes(Document document, AlignRequest request, CancellationToken cancellationToken) =>
        AlignNodes(document, request, null, cancellationToken);

    public AlignResult AlignNodes(Document document, AlignRequest request, FontPolicy? fontPolicy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            var calculator = CreateBoundsCalculator(document, fontPolicy, cancellationToken, out var rented);
            try { return LayoutOperations.Align(document, calculator, request); }
            finally { rented?.Dispose(); }
        }
    }

    public DistributeResult DistributeNodes(Document document, DistributeRequest request) =>
        DistributeNodes(document, request, CancellationToken.None);

    public DistributeResult DistributeNodes(Document document, DistributeRequest request, CancellationToken cancellationToken) =>
        DistributeNodes(document, request, null, cancellationToken);

    public DistributeResult DistributeNodes(Document document, DistributeRequest request, FontPolicy? fontPolicy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            var calculator = CreateBoundsCalculator(document, fontPolicy, cancellationToken, out var rented);
            try { return LayoutOperations.Distribute(document, calculator, request); }
            finally { rented?.Dispose(); }
        }
    }

    public FitResult FitFrame(Document document, FitRequest request) =>
        FitFrame(document, request, CancellationToken.None);

    public FitResult FitFrame(Document document, FitRequest request, CancellationToken cancellationToken) =>
        FitFrame(document, request, null, cancellationToken);

    public FitResult FitFrame(Document document, FitRequest request, FontPolicy? fontPolicy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        lock (DocumentSynchronization.For(document))
        {
            DocumentSynchronization.EnsureCanMutate(document);
            var calculator = CreateBoundsCalculator(document, fontPolicy, cancellationToken, out var rented);
            try { return LayoutOperations.Fit(document, calculator, request); }
            finally { rented?.Dispose(); }
        }
    }

    private BoundsCalculator CreateBoundsCalculator(Document document, FontPolicy? fontPolicy, CancellationToken cancellationToken, out IDisposable? rented)
    {
        var measurer = ResolveMeasurer(fontPolicy, out rented);
        return new(document, DocumentIndex.Build(document), measurer, _tokenService, _pathOps, cancellationToken);
    }

    /// <summary>
    /// The measurer to use for one call. A non-null <paramref name="fontPolicy"/> builds a
    /// policy-bound measurer through the injected factory so measurement matches a policy-bound
    /// render; its disposable, when any, is returned in <paramref name="rented"/> for the caller
    /// to dispose after the call. A null policy (or no factory) reuses the shared default measurer.
    /// </summary>
    private ITextMeasurer ResolveMeasurer(FontPolicy? fontPolicy, out IDisposable? rented)
    {
        rented = null;
        if (fontPolicy is null || _measurerFactory is null)
            return _textMeasurer;

        var measurer = _measurerFactory(fontPolicy);
        rented = measurer as IDisposable;
        return measurer;
    }

    // ---- Design lint ----

    public LintResult Lint(Document document, LintOptions? options = null) =>
        Lint(document, options ?? LintOptions.Default, CancellationToken.None);

    public LintResult Lint(Document document, LintOptions options, CancellationToken cancellationToken) =>
        Lint(document, options, null, cancellationToken);

    public LintResult Lint(Document document, LintOptions options, FontPolicy? fontPolicy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        lock (DocumentSynchronization.For(document))
        {
            var calculator = CreateBoundsCalculator(document, fontPolicy, cancellationToken, out var rented);
            try
            {
                var service = new LintService(document, calculator, _tokenService, options, cancellationToken);
                return service.Run();
            }
            finally { rented?.Dispose(); }
        }
    }

    // ---- Contact sheet ----

    public ContactSheetResult RenderContactSheet(
        IReadOnlyList<ContactSheetEntry> entries,
        ContactSheetOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (_contactSheetRenderer is null)
        {
            throw new DocumentEngineException(EngineErrors.RenderUnavailable,
                "No contact-sheet renderer is registered with the engine");
        }

        // Validate the grid before touching any document so limit errors are cheap.
        ContactSheetLayout.Compute(entries.Count, options);
        foreach (var entry in entries)
        {
            if (entry is null)
                throw new DocumentEngineException(EngineErrors.InvalidArguments, "Contact sheet entries cannot be null");
        }

        return _contactSheetRenderer.Render(entries, options, cancellationToken);
    }
}
