using FTPlus.AgentGraphicDesign.Core.Documents;
using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using FTPlus.AgentGraphicDesign.Core.Export;
using FTPlus.AgentGraphicDesign.Core.Import;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Lint;
using FTPlus.AgentGraphicDesign.Core.Patching;
using FTPlus.AgentGraphicDesign.Core.PathOperations;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Core.Serialization;
using FTPlus.AgentGraphicDesign.Core.Tokens;
using FTPlus.AgentGraphicDesign.Core.Validation;

namespace FTPlus.AgentGraphicDesign.Core;

/// <summary>
/// The single authoritative document API. All surfaces (CLI, MCP, HTTP host,
/// future GUI) call this interface; no client duplicates document logic.
/// </summary>
public interface IDocumentEngine
{
    // ---- Document lifecycle ----

    /// <summary>Create a new document with an empty root frame. Does not persist.</summary>
    Document CreateDocument(string name, double width = 1920, double height = 1080, string? background = null);

    /// <summary>
    /// Create a new document with an empty root frame and deterministic IDs.
    /// When <paramref name="options"/>.Seed is set, IDs are derived deterministically.
    /// Does not persist.
    /// </summary>
    Document CreateDocument(string name, double width, double height, string? background, CreateDocumentOptions? options);

    /// <summary>Load a document from the store by ID. Returns null if not found.</summary>
    Document? LoadDocument(Guid id);

    /// <summary>Cancellation-aware store load.</summary>
    Document? LoadDocument(Guid id, CancellationToken cancellationToken);

    /// <summary>Load a document from a file path. Returns null if missing or unparseable.</summary>
    Document? LoadDocumentFromFile(string filePath);

    /// <summary>Cancellation-aware file load.</summary>
    Document? LoadDocumentFromFile(string filePath, CancellationToken cancellationToken);

    /// <summary>Load a document from a file path with structured error reporting.</summary>
    DeserializeResult TryLoadDocumentFromFile(string filePath);

    /// <summary>Cancellation-aware file load with structured errors.</summary>
    DeserializeResult TryLoadDocumentFromFile(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Resolve an ID-or-path locator: a GUID string loads from the store, an existing
    /// file path loads from disk. Returns null when the document cannot be found.
    /// The handle remembers the origin so <see cref="Save"/> writes back to it.
    /// </summary>
    DocumentHandle? Open(string idOrPath);

    /// <summary>Cancellation-aware locator resolution.</summary>
    DocumentHandle? Open(string idOrPath, CancellationToken cancellationToken);

    /// <summary>
    /// Persist a document to the store by ID. If <paramref name="expectedRevn"/> is provided,
    /// fails with REVISION_CONFLICT if the stored document's revision doesn't match.
    /// </summary>
    void SaveDocument(Document document, long? expectedRevn = null);

    /// <summary>Cancellation-aware store save.</summary>
    void SaveDocument(Document document, long? expectedRevn, CancellationToken cancellationToken);

    /// <summary>Create a document in the store without replacing an existing document.</summary>
    void CreateDocumentInStore(Document document);

    /// <summary>Cancellation-aware store create.</summary>
    void CreateDocumentInStore(Document document, CancellationToken cancellationToken);

    /// <summary>
    /// Persist a document to an explicit file path. If <paramref name="expectedRevn"/> is provided,
    /// fails with REVISION_CONFLICT if the stored document's revision doesn't match.
    /// </summary>
    void SaveDocumentToFile(Document document, string filePath, long? expectedRevn = null);

    /// <summary>Cancellation-aware file save.</summary>
    void SaveDocumentToFile(Document document, string filePath, long? expectedRevn, CancellationToken cancellationToken);

    /// <summary>Create a document file without replacing an existing target.</summary>
    void CreateDocumentFile(Document document, string filePath);

    /// <summary>Cancellation-aware file create.</summary>
    void CreateDocumentFile(Document document, string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Persist a handle back to where it was opened from (file path or store).
    /// If <paramref name="expectedRevn"/> is provided, fails with REVISION_CONFLICT
    /// if the stored document's revision doesn't match.
    /// </summary>
    void Save(DocumentHandle handle, long? expectedRevn = null);

    /// <summary>Cancellation-aware handle save.</summary>
    void Save(DocumentHandle handle, long? expectedRevn, CancellationToken cancellationToken);

    /// <summary>Delete a document from the store.</summary>
    void DeleteDocument(Guid id);

    /// <summary>Cancellation-aware store delete.</summary>
    void DeleteDocument(Guid id, CancellationToken cancellationToken);

    /// <summary>List documents in the store.</summary>
    IEnumerable<DocumentInfo> ListDocuments();

    /// <summary>Cancellation-aware store listing.</summary>
    IEnumerable<DocumentInfo> ListDocuments(CancellationToken cancellationToken);

    // ---- Query ----

    /// <summary>Validate the document against all structural and style invariants.</summary>
    ValidationResult Validate(Document document);

    /// <summary>Validate the document with optional additional profile restrictions.</summary>
    ValidationResult Validate(Document document, ValidationOptions options) =>
        options.Profile == ValidationProfile.Default
            ? Validate(document)
            : DocumentValidator.Validate(document, options);

    /// <summary>Total node count including the root.</summary>
    int CountNodes(Document document);

    /// <summary>Find a node anywhere in the tree by ID. Returns null if absent.</summary>
    Node? FindNode(Document document, Guid nodeId);

    /// <summary>Flatten the tree to a list in document (fractional-index) order.</summary>
    IReadOnlyList<NodeInfo> ListNodes(Document document);

    /// <summary>Bounded document-order subtree/page query with scan/depth budgets and cooperative cancellation.</summary>
    NodePage ListNodesPage(Document document, NodePageQuery query, CancellationToken cancellationToken = default);

    /// <summary>Canonical deterministic JSON for the document.</summary>
    string ToJson(Document document);

    // ---- Mutation (enforces invariants, bumps revn) ----

    /// <summary>
    /// Create a node from <paramref name="spec"/> and append it under the parent
    /// (null parent = root). Assigns ID and fractional order; increments revn.
    /// </summary>
    Node AddNode(Document document, Guid? parentId, NewNodeSpec spec);

    /// <summary>Apply partial updates to a node; increments revn.</summary>
    Node UpdateNode(Document document, Guid nodeId, NodeUpdates updates);

    /// <summary>Move a node to a new parent; increments revn.</summary>
    Node MoveNode(Document document, Guid nodeId, Guid? newParentId);

    /// <summary>
    /// Reorder a node between sibling anchors; <paramref name="beforeNodeId"/> is the
    /// sibling before the desired position and <paramref name="afterNodeId"/> is the
    /// sibling after it. At least one anchor is required. Increments revn.
    /// </summary>
    Node ReorderNode(Document document, Guid nodeId, Guid? beforeNodeId, Guid? afterNodeId);

    /// <summary>Remove a node (never the root); increments revn.</summary>
    void RemoveNode(Document document, Guid nodeId);

    // ---- Patch (atomic multi-operation mutation) ----

    /// <summary>
    /// Apply every operation to the opened document, validate it, and persist it back to
    /// where the handle was opened from with a single revision increment. Any failure
    /// leaves the stored document untouched and names the failing operation index.
    /// </summary>
    PatchResult ApplyPatch(DocumentHandle handle, IReadOnlyList<PatchOperation> operations, long? expectedRevn = null);

    /// <summary>
    /// Cancellation-aware atomic patch. If <paramref name="expectedRevn"/> is provided, the
    /// save fails with REVISION_CONFLICT when the stored revision differs.
    /// </summary>
    PatchResult ApplyPatch(
        DocumentHandle handle,
        IReadOnlyList<PatchOperation> operations,
        long? expectedRevn,
        CancellationToken cancellationToken);

    // ---- Export / render ----

    /// <summary>Export via the registered adapter matching <c>options.Format</c>.</summary>
    Task<ExportResult> ExportAsync(Document document, ExportOptions options, CancellationToken ct = default);

    /// <summary>Render to a raster image via the registered renderer.</summary>
    RenderResult Render(Document document, RenderOptions options);

    /// <summary>Cancellation-aware rendering.</summary>
    RenderResult Render(Document document, RenderOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Export the subtree rooted at <paramref name="nodeId"/> as a standalone image, cropped to
    /// the subtree's own bounds (D-10). The node is exported in isolation: its own transform
    /// and descendants are drawn, but ancestor transforms and ancestor clipping are not applied.
    /// Fails with <c>NODE_NOT_FOUND</c> when the id is absent and <c>NODE_NO_BOUNDS</c> when the
    /// subtree has no visible extent.
    /// </summary>
    Task<ExportResult> ExportNodeAsync(Document document, Guid nodeId, ExportOptions options, CancellationToken ct = default);

    /// <summary>Raster counterpart of <see cref="ExportNodeAsync"/>: render the subtree rooted at <paramref name="nodeId"/>, cropped to its own bounds (D-10).</summary>
    RenderResult RenderNode(Document document, Guid nodeId, RenderOptions options, CancellationToken cancellationToken);

    // ---- Import ----

    /// <summary>
    /// Import an SVG file into an AGD document. Best-effort conversion with
    /// structured warnings for unsupported features.
    /// </summary>
    SvgImportResult ImportSvg(string svgContent, SvgImportOptions? options = null);

    /// <summary>Cancellation-aware SVG import with default options.</summary>
    SvgImportResult ImportSvg(string svgContent, CancellationToken cancellationToken);

    /// <summary>Cancellation-aware SVG import.</summary>
    SvgImportResult ImportSvg(string svgContent, SvgImportOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Import an SVG file from disk into an AGD document.
    /// </summary>
    SvgImportResult ImportSvgFromFile(string filePath, SvgImportOptions? options = null);

    /// <summary>Cancellation-aware SVG file import with default options.</summary>
    SvgImportResult ImportSvgFromFile(string filePath, CancellationToken cancellationToken);

    /// <summary>Cancellation-aware SVG file import.</summary>
    SvgImportResult ImportSvgFromFile(string filePath, SvgImportOptions? options, CancellationToken cancellationToken);

    // ---- Design tokens ----

    /// <summary>
    /// Set a color token in the document.
    /// </summary>
    void SetColorToken(Document document, string name, string color);

    /// <summary>
    /// Remove a color token from the document.
    /// </summary>
    bool RemoveColorToken(Document document, string name);

    /// <summary>
    /// Set a typography token in the document.
    /// </summary>
    void SetTypographyToken(Document document, string name, TypographyToken token);

    /// <summary>
    /// Remove a typography token from the document.
    /// </summary>
    bool RemoveTypographyToken(Document document, string name);

    /// <summary>
    /// Get all color tokens from the document.
    /// </summary>
    IReadOnlyDictionary<string, string> GetColorTokens(Document document);

    /// <summary>
    /// Get all typography tokens from the document.
    /// </summary>
    IReadOnlyDictionary<string, TypographyToken> GetTypographyTokens(Document document);

    /// <summary>
    /// Apply a bundled palette to the document.
    /// </summary>
    PaletteApplyResult ApplyPalette(Document document, string paletteName);

    /// <summary>
    /// List available bundled palettes.
    /// </summary>
    IReadOnlyList<BundledPaletteInfo> ListPalettes();

    /// <summary>
    /// Import tokens from DTCG format JSON.
    /// </summary>
    TokenImportResult ImportDtcgTokens(Document document, string json);

    /// <summary>Cancellation-aware DTCG token import.</summary>
    TokenImportResult ImportDtcgTokens(Document document, string json, CancellationToken cancellationToken);

    /// <summary>
    /// Export tokens to DTCG format JSON.
    /// </summary>
    TokenExportResult ExportDtcgTokens(Document document, bool resolveReferences = false);

    /// <summary>
    /// Resolve a color token reference to its value.
    /// </summary>
    string? ResolveColorRef(Document document, string? reference);

    /// <summary>
    /// Create a copy of the document with all token references resolved.
    /// </summary>
    Document ResolveAllTokenRefs(Document document);

    // ---- Path operations (Clipper2) ----

    /// <summary>
    /// Perform a boolean operation on two SVG paths.
    /// </summary>
    PathOperationResult PathBooleanOp(string subjectPath, string clipPath, BooleanOperation operation);

    /// <summary>Cancellation-aware boolean path operation.</summary>
    PathOperationResult PathBooleanOp(string subjectPath, string clipPath, BooleanOperation operation, CancellationToken cancellationToken);

    /// <summary>Boolean path operation with an explicit fill rule for overlapping input contours.</summary>
    PathOperationResult PathBooleanOp(string subjectPath, string clipPath, BooleanOperation operation, PathFillRule fillRule, CancellationToken cancellationToken);

    /// <summary>
    /// Offset a path to create a stroke outline.
    /// </summary>
    PathOperationResult PathStrokeOffset(string path, double offset, OffsetJoinType joinType = OffsetJoinType.Round, OffsetEndType endType = OffsetEndType.Polygon, double miterLimit = 2.0);

    /// <summary>Cancellation-aware path offset.</summary>
    PathOperationResult PathStrokeOffset(string path, double offset, OffsetJoinType joinType, OffsetEndType endType, double miterLimit, CancellationToken cancellationToken);

    /// <summary>
    /// Path offset with an explicit fill rule. <see cref="PathFillRule.EvenOdd"/> resolves
    /// overlapping contours into outer/hole outlines before offsetting.
    /// </summary>
    PathOperationResult PathStrokeOffset(string path, double offset, OffsetJoinType joinType, OffsetEndType endType, double miterLimit, PathFillRule fillRule, CancellationToken cancellationToken);

    /// <summary>
    /// Union multiple paths into one.
    /// </summary>
    PathOperationResult PathUnionAll(IEnumerable<string> paths);

    /// <summary>Cancellation-aware multi-path union.</summary>
    PathOperationResult PathUnionAll(IEnumerable<string> paths, CancellationToken cancellationToken);

    /// <summary>Multi-path union with an explicit fill rule for overlapping input contours.</summary>
    PathOperationResult PathUnionAll(IEnumerable<string> paths, PathFillRule fillRule, CancellationToken cancellationToken);

    // ---- Layout: bounds and alignment helpers ----

    /// <summary>
    /// Local (untransformed) and world (after every transform, clipped by clipping-frame
    /// ancestors) bounds for one node, or for every node in document order when
    /// <paramref name="nodeId"/> is null. Text uses the registered <see cref="ITextMeasurer"/>
    /// or the glyph-count estimator, flagged through <see cref="NodeBounds.Measured"/>.
    /// </summary>
    IReadOnlyList<NodeBounds> GetBounds(Document document, Guid? nodeId = null);

    /// <summary>Cancellation-aware bounds query.</summary>
    IReadOnlyList<NodeBounds> GetBounds(Document document, Guid? nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Bounds query under a specific <see cref="FontPolicy"/> so measured text matches a render
    /// under the same policy. A null policy uses the default measurer; a non-null policy requires
    /// the engine to be built with a measurer factory, otherwise the policy is ignored.
    /// </summary>
    IReadOnlyList<NodeBounds> GetBounds(Document document, Guid? nodeId, FontPolicy? fontPolicy, CancellationToken cancellationToken);

    /// <summary>
    /// Align nodes by their world bounds to a parent, the selection, or the canvas.
    /// Changes only <c>transform.x</c>/<c>transform.y</c>; increments revn.
    /// </summary>
    AlignResult AlignNodes(Document document, AlignRequest request);

    /// <summary>Cancellation-aware alignment.</summary>
    AlignResult AlignNodes(Document document, AlignRequest request, CancellationToken cancellationToken);

    /// <summary>Alignment whose world bounds are measured under <paramref name="fontPolicy"/> (null = default measurer).</summary>
    AlignResult AlignNodes(Document document, AlignRequest request, FontPolicy? fontPolicy, CancellationToken cancellationToken);

    /// <summary>
    /// Space nodes evenly along one axis by gap or by centre. Changes only
    /// <c>transform.x</c>/<c>transform.y</c>; increments revn.
    /// </summary>
    DistributeResult DistributeNodes(Document document, DistributeRequest request);

    /// <summary>Cancellation-aware distribution.</summary>
    DistributeResult DistributeNodes(Document document, DistributeRequest request, CancellationToken cancellationToken);

    /// <summary>Distribution whose world bounds are measured under <paramref name="fontPolicy"/> (null = default measurer).</summary>
    DistributeResult DistributeNodes(Document document, DistributeRequest request, FontPolicy? fontPolicy, CancellationToken cancellationToken);

    /// <summary>
    /// Resize a frame to enclose its children plus padding, shifting the children so
    /// their world position is unchanged; increments revn.
    /// </summary>
    FitResult FitFrame(Document document, FitRequest request);

    /// <summary>Cancellation-aware frame fit.</summary>
    FitResult FitFrame(Document document, FitRequest request, CancellationToken cancellationToken);

    /// <summary>Frame fit whose child world bounds are measured under <paramref name="fontPolicy"/> (null = default measurer).</summary>
    FitResult FitFrame(Document document, FitRequest request, FontPolicy? fontPolicy, CancellationToken cancellationToken);

    // ---- Design lint ----

    /// <summary>
    /// Advisory design lints: text contrast (LINT001), hairline strokes (LINT002), off-grid
    /// icon geometry (LINT003), content outside the canvas (LINT004), unused tokens (LINT005),
    /// invisible content (LINT006), and symbol readiness (LINT007). Separate from validation:
    /// lint never blocks export and reports only warnings and infos.
    /// </summary>
    LintResult Lint(Document document, LintOptions? options = null);

    /// <summary>Cancellation-aware lint.</summary>
    LintResult Lint(Document document, LintOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Lint whose text-contrast and geometry checks measure text under <paramref name="fontPolicy"/>
    /// so findings match a render under the same policy (null = default measurer).
    /// </summary>
    LintResult Lint(Document document, LintOptions options, FontPolicy? fontPolicy, CancellationToken cancellationToken);

    // ---- Contact sheet ----

    /// <summary>
    /// Render several documents into one PNG grid, each aspect-fitted into a square cell and
    /// optionally labelled. Fails with <see cref="EngineErrors.RenderUnavailable"/> when no
    /// contact-sheet renderer is registered; the grid is validated with
    /// <see cref="ContactSheetLayout.Compute"/> before anything is drawn.
    /// </summary>
    ContactSheetResult RenderContactSheet(IReadOnlyList<ContactSheetEntry> entries, ContactSheetOptions options, CancellationToken cancellationToken);
}

public sealed record DocumentInfo(Guid Id, string Name, DateTimeOffset UpdatedAt);

/// <summary>A loaded document plus the origin it should be saved back to.</summary>
public sealed record DocumentHandle(Document Document, string? SourcePath)
{
    internal bool IsStoreSource { get; init; }
}
