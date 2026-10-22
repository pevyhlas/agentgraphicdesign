using FTPlus.AgentGraphicDesign.Cli.Commands;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Assets;
using FTPlus.AgentGraphicDesign.Core.Layout;
using FTPlus.AgentGraphicDesign.Core.Persistence;
using FTPlus.AgentGraphicDesign.Core.Pixels;
using FTPlus.AgentGraphicDesign.Core.Rendering;
using FTPlus.AgentGraphicDesign.Skia;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using System.Reflection;
using System.Text.Json;

namespace FTPlus.AgentGraphicDesign.Cli;

public static class Program
{
    internal const string FileEngineServiceKey = "file-only";

    public static int Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("agd");
            config.SetApplicationVersion(GetVersion());
            config.UseStrictParsing();
            config.CancellationExitCode(130);
            config.SetExceptionHandler((exception, _) => HandleException(exception, args));

            config.AddCommand<CreateCommand>("create")
                .WithDescription("Create a new scene document")
                .WithExample("create", "my-design")
                .WithExample("create", "banner", "-w", "728", "-h", "90")
                .WithExample("create", "ic-power", "--template", "web-icon-32", "-o", "assets/agd/ic-power.agd.json")
                .WithExample("create", "icon", "-o", "assets/icon.agd.json", "--background", "#ffffff");

            config.AddCommand<InspectCommand>("inspect")
                .WithDescription("Inspect a scene document")
                .WithExample("inspect", "my-design.agd.json")
                .WithExample("inspect", "my-design.agd.json", "--tree")
                .WithExample("inspect", "my-design.agd.json", "-f", "json");

            config.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate a scene document")
                .WithExample("validate", "my-design.agd.json")
                .WithExample("validate", "my-design.agd.json", "--strict")
                .WithExample("validate", "icon.agd.json", "--profile", "web-icon")
                .WithExample("validate", "my-design.agd.json", "-f", "json");

            config.AddCommand<ExportCommand>("export")
                .WithDescription("Export a scene document to SVG or other formats")
                .WithExample("export", "my-design.agd.json")
                .WithExample("export", "my-design.agd.json", "-o", "output.svg")
                .WithExample("export", "icon.agd.json", "--profile", "web-icon", "-o", "icon.svg")
                .WithExample("export", "my-design.agd.json", "-t", "svg", "-s", "2.0")
                .WithExample("export", "icon.agd.json", "--symbol", "ic-power", "-o", "ic-power.symbol.svg");

            config.AddCommand<SpriteCommand>("sprite")
                .WithDescription("Combine scene documents into one SVG symbol sprite sheet")
                .WithExample("sprite", "ic-a.agd.json", "ic-b.agd.json", "-o", "sprite.svg")
                .WithExample("sprite", "ic-a.agd.json", "ic-b.agd.json", "-o", "sprite.svg", "--strict", "--profile", "web-icon");

            config.AddCommand<ImportCommand>("import")
                .WithDescription("Import an SVG file into a scene document")
                .WithExample("import", "logo.svg")
                .WithExample("import", "icon.svg", "-o", "icon.agd.json")
                .WithExample("import", "banner.svg", "-n", "campaign-banner", "-f", "json");

            config.AddCommand<ListCommand>("list")
                .WithDescription("List all nodes in a document")
                .WithExample("list", "my-design.agd.json")
                .WithExample("list", "my-design.agd.json", "--tree")
                .WithExample("list", "my-design.agd.json", "-f", "json");

            config.AddCommand<ListDocumentsCommand>("list-documents")
                .WithDescription("List all .agd.json documents in a directory")
                .WithExample("list-documents")
                .WithExample("list-documents", "-d", "./designs")
                .WithExample("list-documents", "-f", "json");

            config.AddCommand<AddCommand>("add")
                .WithDescription("Add a node to a document")
                .WithExample("add", "my-design.agd.json", "rectangle", "--width", "100", "--height", "50")
                .WithExample("add", "my-design.agd.json", "text", "--text", "Hello", "-x", "50", "-y", "50")
                .WithExample("add", "my-design.agd.json", "ellipse", "-n", "circle1", "--fill", "#ff0000");

            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Update properties of an existing node")
                .WithExample("update", "my-design.agd.json", "<node-id>", "--fill", "#00ff00")
                .WithExample("update", "my-design.agd.json", "<node-id>", "-x", "100", "-y", "200")
                .WithExample("update", "my-design.agd.json", "<node-id>", "--visible", "false");

            config.AddCommand<DeleteCommand>("delete")
                .WithDescription("Delete a node from a document")
                .WithExample("delete", "my-design.agd.json", "<node-id>");

            config.AddCommand<MoveCommand>("move")
                .WithDescription("Move a node to a new parent container")
                .WithExample("move", "my-design.agd.json", "<node-id>", "root")
                .WithExample("move", "my-design.agd.json", "<node-id>", "<parent-id>");

            config.AddCommand<ReorderCommand>("reorder")
                .WithDescription("Reorder a node among its siblings")
                .WithExample("reorder", "my-design.agd.json", "<node-id>", "--before", "<sibling-id>")
                .WithExample("reorder", "my-design.agd.json", "<node-id>", "--after", "<sibling-id>");

            config.AddCommand<BoundsCommand>("bounds")
                .WithDescription("Report local and world bounds for one node or every node")
                .WithExample("bounds", "my-design.agd.json")
                .WithExample("bounds", "my-design.agd.json", "--node", "<node-id>", "-f", "json");

            config.AddCommand<AlignCommand>("align")
                .WithDescription("Align nodes by their world bounds to the parent, the selection, or the canvas")
                .WithExample("align", "my-design.agd.json", "--nodes", "<id-a>,<id-b>", "--mode", "centerX")
                .WithExample("align", "my-design.agd.json", "--nodes", "<node-id>", "--mode", "left", "--relative-to", "parent");

            config.AddCommand<DistributeCommand>("distribute")
                .WithDescription("Space nodes evenly along one axis by gap or by centers")
                .WithExample("distribute", "my-design.agd.json", "--nodes", "<id-a>,<id-b>,<id-c>", "--axis", "horizontal")
                .WithExample("distribute", "my-design.agd.json", "--nodes", "<id-a>,<id-b>", "--axis", "vertical", "--gap", "16");

            config.AddCommand<FitCommand>("fit")
                .WithDescription("Resize a frame to enclose its children plus padding")
                .WithExample("fit", "my-design.agd.json", "--frame", "<frame-id>")
                .WithExample("fit", "my-design.agd.json", "--frame", "<frame-id>", "--padding", "8", "-f", "json");

            config.AddCommand<PatchCommand>("patch")
                .WithDescription("Apply many edits atomically from a JSON operations array")
                .WithExample("patch", "my-design.agd.json", "--file", "ops.json")
                .WithExample("patch", "my-design.agd.json", "--ops", "[{\"op\":\"setCanvas\",\"width\":640}]")
                .WithExample("patch", "my-design.agd.json", "--stdin", "--expected-revn", "3", "-f", "json");

            config.AddCommand<LintCommand>("lint")
                .WithDescription("Report advisory design lints (contrast, hairlines, grid, overflow, unused tokens, invisible content, symbol readiness)")
                .WithExample("lint", "my-design.agd.json")
                .WithExample("lint", "icon.agd.json", "--profile", "web-icon", "--strict-grid", "--fail-on", "warning")
                .WithExample("lint", "my-design.agd.json", "--target-size", "256", "-f", "json");

            config.AddCommand<ContactSheetCommand>("contact-sheet")
                .WithDescription("Render several documents into one PNG grid for visual review")
                .WithExample("contact-sheet", "a.agd.json", "b.agd.json", "-o", "sheet.png")
                .WithExample("contact-sheet", "icons/*.agd.json", "-o", "sheet.png", "--columns", "4", "--cell", "96", "--labels");

            config.AddBranch("assets", assets =>
            {
                assets.SetDescription("Build portable assets from a manifest or check generated output for drift");
                assets.AddCommand<AssetsBuildCommand>("build")
                    .WithDescription("Validate and generate all manifest outputs without pruning unexpected files")
                    .WithExample("assets", "build", "assets.json");
                assets.AddCommand<AssetsCheckCommand>("check")
                    .WithDescription("Regenerate in memory and check output bytes/inventory without writing files")
                    .WithExample("assets", "check", "assets.json", "-f", "json");
                assets.AddCommand<AssetsPreflightCommand>("preflight")
                    .WithDescription("Check artifact encoded integrity and PNG structure without decoding or admission")
                    .WithExample("assets", "preflight", "bundle/descriptor.json", "-f", "json");
                assets.AddCommand<AssetsVerifyCommand>("verify")
                    .WithDescription("Verify exact RGBA8 samples using managed-in-process decoding without admission")
                    .WithExample("assets", "verify", "bundle/descriptor.json", "-f", "json");
            });

            config.AddBranch("path", path =>
            {
                path.SetDescription("Path boolean operations and stroke offsetting (Clipper2)");

                path.AddCommand<PathBooleanCommand>("bool")
                    .WithDescription("Perform a boolean operation on two paths")
                    .WithExample("path", "bool", "union", "\"M0,0 L100,0 L100,100 L0,100 Z\"", "\"M50,50 L150,50 L150,150 L50,150 Z\"")
                    .WithExample("path", "bool", "intersect", "\"M0,0 L100,0 L100,100 Z\"", "\"M0,0 L100,100 L0,100 Z\"")
                    .WithExample("path", "bool", "difference", "\"M0,0 L200,0 L200,200 L0,200 Z\"", "\"M50,50 L150,50 L150,150 L50,150 Z\"");

                path.AddCommand<PathOffsetCommand>("offset")
                    .WithDescription("Offset a path to create a stroke outline")
                    .WithExample("path", "offset", "\"M0,0 L100,0 L100,100 L0,100 Z\"", "5")
                    .WithExample("path", "offset", "\"M0,0 L100,0 L100,100 L0,100 Z\"", "-3", "--join", "miter");

                path.AddCommand<PathUnionCommand>("union")
                    .WithDescription("Union multiple paths into one")
                    .WithExample("path", "union", "\"M0,0 L50,0 L50,50 L0,50 Z\"", "\"M25,25 L75,25 L75,75 L25,75 Z\"");
            });

            config.AddBranch("tokens", tokens =>
            {
                tokens.SetDescription("Design token operations (DTCG format)");

                tokens.AddCommand<TokensSetColorCommand>("set-color")
                    .WithDescription("Set a color token")
                    .WithExample("tokens", "set-color", "my-design.agd.json", "primary", "#0066cc")
                    .WithExample("tokens", "set-color", "my-design.agd.json", "brand-red", "#dc2626", "-f", "json");

                tokens.AddCommand<TokensRemoveColorCommand>("remove-color")
                    .WithDescription("Remove a color token")
                    .WithExample("tokens", "remove-color", "my-design.agd.json", "primary");

                tokens.AddCommand<TokensListColorsCommand>("list-colors")
                    .WithDescription("List color tokens in a document")
                    .WithExample("tokens", "list-colors", "my-design.agd.json")
                    .WithExample("tokens", "list-colors", "my-design.agd.json", "-f", "json");

                tokens.AddCommand<TokensApplyPaletteCommand>("apply-palette")
                    .WithDescription("Apply a bundled palette to a document")
                    .WithExample("tokens", "apply-palette", "my-design.agd.json", "tailwind")
                    .WithExample("tokens", "apply-palette", "my-design.agd.json", "material");

                tokens.AddCommand<TokensListPalettesCommand>("list-palettes")
                    .WithDescription("List available bundled palettes")
                    .WithExample("tokens", "list-palettes")
                    .WithExample("tokens", "list-palettes", "-f", "json");

                tokens.AddCommand<TokensImportCommand>("import")
                    .WithDescription("Import tokens from a DTCG JSON file")
                    .WithExample("tokens", "import", "my-design.agd.json", "tokens.json");

                tokens.AddCommand<TokensExportCommand>("export")
                    .WithDescription("Export tokens to DTCG JSON format")
                    .WithExample("tokens", "export", "my-design.agd.json")
                    .WithExample("tokens", "export", "my-design.agd.json", "-o", "design-tokens.json", "--resolve-refs");
            });

            config.AddBranch("pixels", pixels =>
            {
                pixels.SetDescription("Author and evaluate native RGBA8 pixel assets (source directory as the target)");
                pixels.AddCommand<PixelImportCommand>("import")
                    .WithDescription("Import and preserve one provided PNG still as an editable native asset (managed local)")
                    .WithExample("pixels", "import", "assets/icon.pixels", "--descriptor", "bundle/artifact.json", "--name", "icon", "--layer-name", "Imported");
                pixels.AddCommand<PixelImportLayerCommand>("import-layer")
                    .WithDescription("Import and preserve an independent same-size layer under an expected snapshot")
                    .WithExample("pixels", "import-layer", "assets/icon.pixels", "--request", "import-layer.json");
                pixels.AddCommand<PixelCreateCommand>("create")
                    .WithDescription("Create a native pixel asset")
                    .WithExample("pixels", "create", "assets/icon.pixels", "--name", "icon", "--width", "32", "--height", "32");
                pixels.AddCommand<PixelInspectCommand>("inspect")
                    .WithDescription("Inspect a pixel asset's metadata and captured source token")
                    .WithExample("pixels", "inspect", "assets/icon.pixels", "-f", "json");
                pixels.AddCommand<PixelRegionCommand>("region")
                    .WithDescription("Read actual bounded samples from a layer's pixels or mask")
                    .WithExample("pixels", "region", "assets/icon.pixels", "--layer", "<GUID>", "--target", "pixels", "--rect", "0,0,8,8");
                pixels.AddCommand<PixelEditCommand>("edit")
                    .WithDescription("Apply a guarded typed edit batch from a request JSON file")
                    .WithExample("pixels", "edit", "assets/icon.pixels", "--request", "edit.json");
                pixels.AddCommand<PixelValidateCommand>("validate")
                    .WithDescription("Run native validation and report a completed valid/invalid assessment")
                    .WithExample("pixels", "validate", "assets/icon.pixels", "-f", "json");
                pixels.AddCommand<PixelPreviewCommand>("preview")
                    .WithDescription("Evaluate and write a preview PNG (explicit file write, no source change)")
                    .WithExample("pixels", "preview", "assets/icon.pixels", "--output", "preview.png");
                pixels.AddCommand<PixelExportCommand>("export")
                    .WithDescription("Evaluate and publish an export PNG to a confined path (no source change)")
                    .WithExample("pixels", "export", "assets/icon.pixels", "--output", "icon.png", "--overwrite");
                pixels.AddCommand<PixelCheckCommand>("check")
                    .WithDescription("Compare an existing output PNG against a fresh evaluation (exit 6 on drift)")
                    .WithExample("pixels", "check", "assets/icon.pixels", "--output", "icon.png", "-f", "json");
            });
        });

        return app.Run(args);
    }

    private static int HandleException(Exception exception, string[] args)
    {
        var format = RequestedFormat(args);
        // Command construction failures (an overlapping .agd store, an unwritable directory) arrive
        // wrapped as "Could not resolve type"; report the underlying failure instead.
        if (exception is CommandRuntimeException && FindConstructionFailure(exception) is { } inner)
            exception = inner;

        var (code, exitCode, message) = exception switch
        {
            CommandParseException => ("INVALID_ARGUMENTS", ExitCodes.InvalidArguments, exception.Message),
            CommandRuntimeException => ("INVALID_ARGUMENTS", ExitCodes.InvalidArguments, exception.Message),
            DocumentEngineException engineException => (engineException.Code, ExitCodes.GeneralError, engineException.Message),
            OperationCanceledException => ("CANCELLED", 130, "Operation cancelled"),
            UnauthorizedAccessException => ("ACCESS_DENIED", ExitCodes.GeneralError, "Access denied"),
            FileNotFoundException => ("FILE_NOT_FOUND", ExitCodes.GeneralError, exception.Message),
            DirectoryNotFoundException => ("DIRECTORY_NOT_FOUND", ExitCodes.GeneralError, exception.Message),
            JsonException => ("SERIALIZATION_ERROR", ExitCodes.GeneralError, exception.Message),
            IOException => ("IO_ERROR", ExitCodes.GeneralError, exception.Message),
            NotSupportedException => ("INVALID_ARGUMENTS", ExitCodes.InvalidArguments, exception.Message),
            InvalidOperationException => (EngineErrors.InvalidProperty, ExitCodes.GeneralError, exception.Message),
            _ => ("UNEXPECTED_ERROR", ExitCodes.GeneralError, "The operation failed unexpectedly")
        };

        AgentOutput.WriteError(code, message, format);
        return exitCode;
    }

    private static Exception? FindConstructionFailure(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is DocumentEngineException or UnauthorizedAccessException or IOException)
                return current;
        }

        return null;
    }

    private static OutputFormat RequestedFormat(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals("--format=json", StringComparison.OrdinalIgnoreCase))
            {
                return OutputFormat.Json;
            }

            if ((args[i] is "-f" or "--format") &&
                i + 1 < args.Count &&
                args[i + 1].Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                return OutputFormat.Json;
            }
        }

        return OutputFormat.Text;
    }

    private static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IDocumentStore>(sp =>
        {
            var basePath = Path.Combine(Environment.CurrentDirectory, ".agd");
            return new FileDocumentStore(basePath);
        });
        services.AddSingleton<IRenderer, SkiaRenderer>();
        services.AddSingleton<ITextMeasurer, SkiaTextMeasurer>();
        services.AddSingleton<IContactSheetRenderer>(sp => new SkiaContactSheetRenderer(sp.GetRequiredService<IRenderer>()));
        // Native pixel operation surface: construction is I/O-free (no discovery, store or hidden state).
        services.AddSingleton<NativePixelService>();
        // File validation must also work inside an MCP store or a read-only cwd.
        services.AddKeyedSingleton<IDocumentEngine>(FileEngineServiceKey, (sp, _) =>
            new DocumentEngine(
                renderer: sp.GetRequiredService<IRenderer>(),
                textMeasurer: sp.GetRequiredService<ITextMeasurer>()));
        // Batch checks are file-only: resolving their engine must not create a .agd store.
        services.AddSingleton<AssetPipeline>(sp =>
        {
            var engine = sp.GetRequiredKeyedService<IDocumentEngine>(FileEngineServiceKey);
            return new AssetPipeline(engine, new VectorAssetProducer(engine));
        });
        services.AddSingleton<IDocumentEngine>(sp =>
        {
            var store = sp.GetRequiredService<IDocumentStore>();
            var renderer = sp.GetRequiredService<IRenderer>();
            var textMeasurer = sp.GetRequiredService<ITextMeasurer>();
            var contactSheetRenderer = sp.GetRequiredService<IContactSheetRenderer>();
            return new DocumentEngine(store, exportAdapters: null, renderer, textMeasurer, contactSheetRenderer,
                measurerFactory: policy => new SkiaTextMeasurer(policy));
        });
    }
}
