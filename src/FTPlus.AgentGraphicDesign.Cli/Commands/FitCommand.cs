using System.ComponentModel;
using FTPlus.AgentGraphicDesign.Cli.Infrastructure;
using FTPlus.AgentGraphicDesign.Cli.Output;
using FTPlus.AgentGraphicDesign.Core;
using FTPlus.AgentGraphicDesign.Core.Layout;
using Spectre.Console.Cli;

namespace FTPlus.AgentGraphicDesign.Cli.Commands;

public sealed class FitCommand : Command<FitCommand.Settings>
{
    private readonly IDocumentEngine _engine;

    public FitCommand(IDocumentEngine engine)
    {
        _engine = engine;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<DOCUMENT>")]
        [Description("Document file path or ID")]
        public required string Document { get; init; }

        [CommandOption("--frame <ID>")]
        [Description("Frame node ID (GUID) to resize around its children (required)")]
        public string? Frame { get; init; }

        [CommandOption("--padding <N>")]
        [Description("Padding around the children (default: 0)")]
        [DefaultValue(0.0)]
        public double Padding { get; init; }

        [CommandOption("-f|--format <FORMAT>")]
        [Description("Output format: text, json (default: text)")]
        public string? Format { get; init; }

        [CommandOption("--expected-revn <REVN>")]
        [Description("Expected document revision (optimistic concurrency)")]
        public long? ExpectedRevn { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken ct)
    {
        var parseResult = OutputFormatExtensions.Parse(settings.Format);
        if (!parseResult.Success)
        {
            AgentOutput.WriteError("INVALID_FORMAT", parseResult.Error!, OutputFormat.Json);
            return ExitCodes.InvalidArguments;
        }

        var format = parseResult.Format;

        if (settings.ExpectedRevn is < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Expected revision cannot be negative", format);
            return ExitCodes.InvalidArguments;
        }

        if (string.IsNullOrWhiteSpace(settings.Frame))
        {
            AgentOutput.WriteError(EngineErrors.InvalidArguments, "--frame is required", format);
            return ExitCodes.InvalidArguments;
        }

        if (!SurfaceIO.IsFinite(settings.Padding) || settings.Padding < 0)
        {
            AgentOutput.WriteError(EngineErrors.InvalidProperty, "Padding must be finite and non-negative", format);
            return ExitCodes.InvalidArguments;
        }

        var handle = SurfaceIO.OpenDocument(_engine, settings.Document, ct);
        if (handle is null)
        {
            AgentOutput.WriteError(EngineErrors.DocumentNotFound, $"Document not found: {settings.Document}", format);
            return ExitCodes.GeneralError;
        }

        if (!Guid.TryParse(settings.Frame, out var frameId))
        {
            AgentOutput.WriteError(EngineErrors.NodeNotFound, $"Invalid frame ID: {settings.Frame}", format);
            return ExitCodes.GeneralError;
        }

        try
        {
            var result = _engine.FitFrame(handle.Document, new FitRequest
            {
                FrameId = frameId,
                Padding = settings.Padding
            }, ct);

            SurfaceIO.Save(_engine, handle, settings.ExpectedRevn, ct);

            AgentOutput.WriteSuccess(
                $"Fitted frame {result.FrameId} to {result.Frame.Width}x{result.Frame.Height} at {result.Frame.X},{result.Frame.Y}",
                format,
                new
                {
                    frameId = result.FrameId,
                    frameName = result.FrameName,
                    x = result.Frame.X,
                    y = result.Frame.Y,
                    width = result.Frame.Width,
                    height = result.Frame.Height,
                    previous = LayoutOutput.Box(result.Previous),
                    children = result.Children.Select(LayoutOutput.Placement),
                    documentRevn = handle.Document.Revn
                });
            return ExitCodes.Success;
        }
        catch (DocumentEngineException ex)
        {
            AgentOutput.WriteError(ex.Code, ex.Message, format);
            return ExitCodes.GeneralError;
        }
    }
}
