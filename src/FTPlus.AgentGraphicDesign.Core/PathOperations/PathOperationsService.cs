using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Clipper2Lib;
using FTPlus.AgentGraphicDesign.Core.Layout;

namespace FTPlus.AgentGraphicDesign.Core.PathOperations;

public enum BooleanOperation
{
    Union,
    Intersect,
    Difference,
    Xor
}

/// <summary>
/// Fill rule used to interpret overlapping or self-intersecting input contours before a
/// geometry operation. <see cref="NonZero"/> preserves the historical behavior.
/// </summary>
public enum PathFillRule
{
    NonZero,
    EvenOdd
}

public static class PathFillRules
{
    /// <summary>Case-insensitive parse of a surface input (<c>nonzero</c> or <c>evenodd</c>).</summary>
    public static bool TryParse(string? value, out PathFillRule fillRule)
    {
        fillRule = value?.ToLowerInvariant() switch
        {
            "nonzero" => PathFillRule.NonZero,
            "evenodd" => PathFillRule.EvenOdd,
            _ => (PathFillRule)(-1)
        };
        return (int)fillRule >= 0;
    }

    public static string ToName(PathFillRule fillRule) =>
        fillRule == PathFillRule.EvenOdd ? "evenodd" : "nonzero";
}

public sealed record PathOperationResult
{
    public required bool Success { get; init; }
    public string? PathData { get; init; }
    public string? Error { get; init; }

    public static PathOperationResult Ok(string pathData) =>
        new() { Success = true, PathData = pathData };

    public static PathOperationResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>Extents of a parsed path; <see cref="Error"/> carries the parser diagnostic on failure.</summary>
public sealed record PathBoundsResult
{
    public required bool Success { get; init; }
    public BoundingBox? Bounds { get; init; }
    public string? Error { get; init; }

    public static PathBoundsResult Ok(BoundingBox bounds) =>
        new() { Success = true, Bounds = bounds };

    public static PathBoundsResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Strict SVG path parsing followed by Clipper2 boolean and offset operations.
/// Curves, including elliptical arcs, are deterministically flattened.
/// </summary>
public sealed partial class PathOperationsService
{
    private const double Scale = 1000.0;
    public const int MaxPathDataCharacters = 1_000_000;
    public const int MaxCommandCount = 10_000;
    public const int MaxFlattenedPointCount = 250_000;
    public const int MaxInputPathCount = 10_000;
    public const int MaxAggregatePathDataCharacters = 2_000_000;
    public const int MaxAggregateCommandCount = 20_000;
    public const int MaxOutputPointCount = 500_000;
    public const int MaxOutputCharacters = 16_000_000;
    public const double MaxCoordinateMagnitude = 1_000_000_000;
    public const double MaxOffsetMagnitude = 1_000_000;

    [GeneratedRegex(@"\G[+-]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public PathOperationResult BooleanOp(string subjectPath, string clipPath, BooleanOperation operation) =>
        BooleanOp(subjectPath, clipPath, operation, PathFillRule.NonZero, CancellationToken.None);

    public PathOperationResult BooleanOp(
        string subjectPath,
        string clipPath,
        BooleanOperation operation,
        CancellationToken cancellationToken) =>
        BooleanOp(subjectPath, clipPath, operation, PathFillRule.NonZero, cancellationToken);

    public PathOperationResult BooleanOp(
        string subjectPath,
        string clipPath,
        BooleanOperation operation,
        PathFillRule fillRule,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectPath))
            return PathOperationResult.Fail("Subject path is empty");
        if (string.IsNullOrWhiteSpace(clipPath))
            return PathOperationResult.Fail("Clip path is empty");
        if (!Enum.IsDefined(operation))
            return PathOperationResult.Fail($"Unknown boolean operation: {operation}");
        if (!Enum.IsDefined(fillRule))
            return PathOperationResult.Fail($"Unknown fill rule: {fillRule}");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var budget = new PathOperationBudget(cancellationToken);
            var subjectPaths = ParseSvgPath(subjectPath, budget);
            var clipPaths = ParseSvgPath(clipPath, budget);

            if (subjectPaths.Count == 0)
                return PathOperationResult.Fail("Subject path contains no valid subpaths");
            if (clipPaths.Count == 0)
                return PathOperationResult.Fail("Clip path contains no valid subpaths");

            var clipper = new Clipper64();
            clipper.AddSubject(subjectPaths);
            clipper.AddClip(clipPaths);

            var solution = new Paths64();
            cancellationToken.ThrowIfCancellationRequested();
            clipper.Execute(operation switch
            {
                BooleanOperation.Union => ClipType.Union,
                BooleanOperation.Intersect => ClipType.Intersection,
                BooleanOperation.Difference => ClipType.Difference,
                BooleanOperation.Xor => ClipType.Xor,
                _ => throw new InvalidOperationException()
            }, ToClipperFillRule(fillRule), solution);
            cancellationToken.ThrowIfCancellationRequested();

            return PathOperationResult.Ok(solution.Count == 0 ? string.Empty : PathsToSvgPath(solution, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PathParseException ex)
        {
            return PathOperationResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PathOperationResult.Fail($"Boolean operation failed: {ex.Message}");
        }
    }

    public PathOperationResult StrokeOffset(
        string path,
        double offset,
        OffsetJoinType joinType = OffsetJoinType.Round,
        OffsetEndType endType = OffsetEndType.Polygon,
        double miterLimit = 2.0) =>
        StrokeOffset(path, offset, joinType, endType, miterLimit, CancellationToken.None);

    public PathOperationResult StrokeOffset(
        string path,
        double offset,
        OffsetJoinType joinType,
        OffsetEndType endType,
        double miterLimit,
        CancellationToken cancellationToken) =>
        StrokeOffset(path, offset, joinType, endType, miterLimit, PathFillRule.NonZero, cancellationToken);

    public PathOperationResult StrokeOffset(
        string path,
        double offset,
        OffsetJoinType joinType,
        OffsetEndType endType,
        double miterLimit,
        PathFillRule fillRule,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathOperationResult.Fail("Path is empty");
        if (!Enum.IsDefined(fillRule))
            return PathOperationResult.Fail($"Unknown fill rule: {fillRule}");
        if (!double.IsFinite(offset) || Math.Abs(offset) > MaxOffsetMagnitude)
            return PathOperationResult.Fail($"Offset must be finite and within +/-{MaxOffsetMagnitude.ToString(CultureInfo.InvariantCulture)}");
        if (!double.IsFinite(miterLimit) || miterLimit <= 0 || miterLimit > 1000)
            return PathOperationResult.Fail("Miter limit must be finite and in the range (0, 1000]");
        if (!Enum.IsDefined(joinType))
            return PathOperationResult.Fail($"Unknown offset join type: {joinType}");
        if (!Enum.IsDefined(endType))
            return PathOperationResult.Fail($"Unknown offset end type: {endType}");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = ParseSvgPath(path, new PathOperationBudget(cancellationToken));
            if (parsed.Count == 0)
                return PathOperationResult.Fail("Path contains no valid subpaths");

            // A zero offset is still fully parsed so malformed input can never
            // bypass validation. Preserve the source because the
            // canonical polygon form would incorrectly close an open path.
            if (offset == 0)
                return PathOperationResult.Ok(path);

            // ClipperOffset has no fill-rule input: it treats every closed contour as solid
            // and unions the offsets. Resolve even-odd overlaps into explicit outer/hole
            // contours first so a same-winding inner contour offsets as a hole.
            if (fillRule == PathFillRule.EvenOdd)
            {
                cancellationToken.ThrowIfCancellationRequested();
                parsed = Clipper.Union(parsed, FillRule.EvenOdd);
                if (parsed.Count == 0)
                    return PathOperationResult.Ok(string.Empty);
            }

            var clipperOffset = new ClipperOffset(miterLimit);
            clipperOffset.AddPaths(parsed, joinType switch
            {
                OffsetJoinType.Miter => JoinType.Miter,
                OffsetJoinType.Round => JoinType.Round,
                OffsetJoinType.Square => JoinType.Square,
                _ => throw new InvalidOperationException()
            }, endType switch
            {
                OffsetEndType.Polygon => EndType.Polygon,
                OffsetEndType.Joined => EndType.Joined,
                OffsetEndType.Butt => EndType.Butt,
                OffsetEndType.Square => EndType.Square,
                OffsetEndType.Round => EndType.Round,
                _ => throw new InvalidOperationException()
            });

            var solution = new Paths64();
            cancellationToken.ThrowIfCancellationRequested();
            clipperOffset.Execute(offset * Scale, solution);
            cancellationToken.ThrowIfCancellationRequested();
            return PathOperationResult.Ok(solution.Count == 0 ? string.Empty : PathsToSvgPath(solution, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PathParseException ex)
        {
            return PathOperationResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PathOperationResult.Fail($"Stroke offset failed: {ex.Message}");
        }
    }

    public PathOperationResult UnionAll(IEnumerable<string> paths) =>
        UnionAll(paths, PathFillRule.NonZero, CancellationToken.None);

    public PathOperationResult UnionAll(IEnumerable<string> paths, CancellationToken cancellationToken) =>
        UnionAll(paths, PathFillRule.NonZero, cancellationToken);

    public PathOperationResult UnionAll(IEnumerable<string> paths, PathFillRule fillRule, CancellationToken cancellationToken)
    {
        if (paths is null)
            return PathOperationResult.Fail("Paths collection is null");
        if (!Enum.IsDefined(fillRule))
            return PathOperationResult.Fail($"Unknown fill rule: {fillRule}");

        var pathList = new List<string>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pathList.Count >= MaxInputPathCount)
                return PathOperationResult.Fail($"Path count exceeds maximum ({MaxInputPathCount})");
            pathList.Add(path);
        }
        if (pathList.Count == 0)
            return PathOperationResult.Fail("No valid paths provided");
        if (pathList.Any(string.IsNullOrWhiteSpace))
            return PathOperationResult.Fail("Paths collection contains an empty path");

        try
        {
            var budget = new PathOperationBudget(cancellationToken);
            var allPaths = new Paths64();
            foreach (var pathData in pathList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parsed = ParseSvgPath(pathData, budget);
                if (parsed.Count == 0)
                    return PathOperationResult.Fail("A path contains no valid subpaths");
                allPaths.AddRange(parsed);
            }

            // A single nonzero input is echoed back untouched (historical behavior); an
            // even-odd input still needs the union so its overlaps resolve into holes.
            if (pathList.Count == 1 && fillRule == PathFillRule.NonZero)
                return PathOperationResult.Ok(PathsToSvgPath(allPaths, cancellationToken));

            var clipper = new Clipper64();
            clipper.AddSubject(allPaths);
            var solution = new Paths64();
            cancellationToken.ThrowIfCancellationRequested();
            clipper.Execute(ClipType.Union, ToClipperFillRule(fillRule), solution);
            cancellationToken.ThrowIfCancellationRequested();
            return PathOperationResult.Ok(solution.Count == 0 ? string.Empty : PathsToSvgPath(solution, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PathParseException ex)
        {
            return PathOperationResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PathOperationResult.Fail($"Union failed: {ex.Message}");
        }
    }

    private static FillRule ToClipperFillRule(PathFillRule fillRule) =>
        fillRule == PathFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero;

    /// <summary>
    /// Axis-aligned extents of a path in user units, using the same strict parser and
    /// curve flattening as the boolean operations. Invalid paths fail with the
    /// parser's diagnostic instead of guessing.
    /// </summary>
    public PathBoundsResult GetBounds(string pathData) =>
        GetBounds(pathData, CancellationToken.None);

    public PathBoundsResult GetBounds(string pathData, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pathData))
            return PathBoundsResult.Fail("Path is empty");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = ParseSvgPath(pathData, new PathOperationBudget(cancellationToken));
            var minX = long.MaxValue;
            var minY = long.MaxValue;
            var maxX = long.MinValue;
            var maxY = long.MinValue;
            var any = false;
            foreach (var path in parsed)
            {
                foreach (var point in path)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                    any = true;
                }
            }

            if (!any)
                return PathBoundsResult.Fail("Path contains no valid subpaths");
            return PathBoundsResult.Ok(BoundingBox.FromEdges(minX / Scale, minY / Scale, maxX / Scale, maxY / Scale));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PathParseException ex)
        {
            return PathBoundsResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return PathBoundsResult.Fail($"Path bounds failed: {ex.Message}");
        }
    }

    private static Paths64 ParseSvgPath(string pathData, PathOperationBudget budget)
    {
        var tokens = Tokenize(pathData, budget);
        var paths = new Paths64();
        var currentPath = new Path64();
        var tokenIndex = 0;
        var commandCount = 0;
        var currentX = 0d;
        var currentY = 0d;
        var startX = 0d;
        var startY = 0d;
        var lastCubicControlX = 0d;
        var lastCubicControlY = 0d;
        var lastQuadraticControlX = 0d;
        var lastQuadraticControlY = 0d;
        var previousCommand = '\0';
        char command = '\0';
        var args = new double[7];

        while (tokenIndex < tokens.Count)
        {
            if (tokens[tokenIndex].Command is { } nextCommand)
            {
                command = nextCommand;
                tokenIndex++;
            }
            else if (command == '\0' || command is 'Z' or 'z')
            {
                throw new PathParseException($"Path data at offset {tokens[tokenIndex].Offset} is missing a command");
            }

            var argumentStart = tokenIndex;
            while (tokenIndex < tokens.Count && tokens[tokenIndex].Command is null)
                tokenIndex++;
            var argumentCount = tokenIndex - argumentStart;

            var arity = command switch
            {
                'M' or 'm' or 'L' or 'l' or 'T' or 't' => 2,
                'H' or 'h' or 'V' or 'v' => 1,
                'C' or 'c' => 6,
                'S' or 's' or 'Q' or 'q' => 4,
                'A' or 'a' => 7,
                'Z' or 'z' => 0,
                _ => throw new PathParseException($"Unsupported SVG path command '{command}'")
            };

            if (arity == 0)
            {
                ChargeCommand(budget, ref commandCount);
                if (argumentCount != 0)
                    throw new PathParseException($"Command '{command}' does not accept arguments");
                if (currentPath.Count == 0)
                    throw new PathParseException("Close-path command has no active subpath");
                currentX = startX;
                currentY = startY;
                paths.Add(currentPath);
                currentPath = new Path64();
                previousCommand = command;
                command = '\0';
                continue;
            }

            if (argumentCount == 0 || argumentCount % arity != 0)
                throw new PathParseException($"Command '{command}' requires complete groups of {arity} arguments; got {argumentCount}");

            if (command is not ('M' or 'm') && currentPath.Count == 0)
                throw new PathParseException($"Command '{command}' appears before a move command");

            for (var i = argumentStart; i < tokenIndex; i += arity)
            {
                ChargeCommand(budget, ref commandCount);
                for (var j = 0; j < arity; j++)
                    args[j] = tokens[i + j].Number!.Value;

                switch (command)
                {
                    case 'M':
                    case 'm':
                        {
                            if (i == argumentStart)
                            {
                                if (currentPath.Count > 0)
                                    paths.Add(currentPath);
                                currentPath = new Path64();
                                if (command == 'm')
                                {
                                    currentX += args[0];
                                    currentY += args[1];
                                }
                                else
                                {
                                    currentX = args[0];
                                    currentY = args[1];
                                }
                                startX = currentX;
                                startY = currentY;
                                AddPoint(currentPath, currentX, currentY, budget);
                            }
                            else
                            {
                                if (command == 'm')
                                {
                                    currentX += args[0];
                                    currentY += args[1];
                                }
                                else
                                {
                                    currentX = args[0];
                                    currentY = args[1];
                                }
                                AddPoint(currentPath, currentX, currentY, budget);
                            }
                            break;
                        }
                    case 'L':
                    case 'l':
                        SetEndpoint(command == 'l', args[0], args[1], ref currentX, ref currentY);
                        AddPoint(currentPath, currentX, currentY, budget);
                        break;
                    case 'H':
                    case 'h':
                        currentX = command == 'h' ? currentX + args[0] : args[0];
                        AddPoint(currentPath, currentX, currentY, budget);
                        break;
                    case 'V':
                    case 'v':
                        currentY = command == 'v' ? currentY + args[0] : args[0];
                        AddPoint(currentPath, currentX, currentY, budget);
                        break;
                    case 'C':
                    case 'c':
                        {
                            var relative = command == 'c';
                            var x1 = relative ? currentX + args[0] : args[0];
                            var y1 = relative ? currentY + args[1] : args[1];
                            var x2 = relative ? currentX + args[2] : args[2];
                            var y2 = relative ? currentY + args[3] : args[3];
                            var x3 = relative ? currentX + args[4] : args[4];
                            var y3 = relative ? currentY + args[5] : args[5];
                            AddCubic(currentPath, currentX, currentY, x1, y1, x2, y2, x3, y3, budget);
                            currentX = x3;
                            currentY = y3;
                            lastCubicControlX = x2;
                            lastCubicControlY = y2;
                            break;
                        }
                    case 'S':
                    case 's':
                        {
                            var x1 = previousCommand is 'C' or 'c' or 'S' or 's' ? 2 * currentX - lastCubicControlX : currentX;
                            var y1 = previousCommand is 'C' or 'c' or 'S' or 's' ? 2 * currentY - lastCubicControlY : currentY;
                            var relative = command == 's';
                            var x2 = relative ? currentX + args[0] : args[0];
                            var y2 = relative ? currentY + args[1] : args[1];
                            var x3 = relative ? currentX + args[2] : args[2];
                            var y3 = relative ? currentY + args[3] : args[3];
                            AddCubic(currentPath, currentX, currentY, x1, y1, x2, y2, x3, y3, budget);
                            currentX = x3;
                            currentY = y3;
                            lastCubicControlX = x2;
                            lastCubicControlY = y2;
                            break;
                        }
                    case 'Q':
                    case 'q':
                        {
                            var relative = command == 'q';
                            var x1 = relative ? currentX + args[0] : args[0];
                            var y1 = relative ? currentY + args[1] : args[1];
                            var x2 = relative ? currentX + args[2] : args[2];
                            var y2 = relative ? currentY + args[3] : args[3];
                            AddQuadratic(currentPath, currentX, currentY, x1, y1, x2, y2, budget);
                            currentX = x2;
                            currentY = y2;
                            lastQuadraticControlX = x1;
                            lastQuadraticControlY = y1;
                            break;
                        }
                    case 'T':
                    case 't':
                        {
                            var x1 = previousCommand is 'Q' or 'q' or 'T' or 't' ? 2 * currentX - lastQuadraticControlX : currentX;
                            var y1 = previousCommand is 'Q' or 'q' or 'T' or 't' ? 2 * currentY - lastQuadraticControlY : currentY;
                            var x2 = command == 't' ? currentX + args[0] : args[0];
                            var y2 = command == 't' ? currentY + args[1] : args[1];
                            AddQuadratic(currentPath, currentX, currentY, x1, y1, x2, y2, budget);
                            currentX = x2;
                            currentY = y2;
                            lastQuadraticControlX = x1;
                            lastQuadraticControlY = y1;
                            break;
                        }
                    case 'A':
                    case 'a':
                        {
                            var x = command == 'a' ? currentX + args[5] : args[5];
                            var y = command == 'a' ? currentY + args[6] : args[6];
                            AddArc(currentPath, currentX, currentY, args[0], args[1], args[2],
                                args[3] == 1, args[4] == 1, x, y, budget);
                            currentX = x;
                            currentY = y;
                            break;
                        }
                }

                previousCommand = command;
                if (command is not ('C' or 'c' or 'S' or 's'))
                {
                    lastCubicControlX = currentX;
                    lastCubicControlY = currentY;
                }
                if (command is not ('Q' or 'q' or 'T' or 't'))
                {
                    lastQuadraticControlX = currentX;
                    lastQuadraticControlY = currentY;
                }
            }
        }

        if (currentPath.Count > 0)
            paths.Add(currentPath);
        return paths;
    }

    private static void ChargeCommand(PathOperationBudget budget, ref int commandCount)
    {
        if (++commandCount > MaxCommandCount)
            throw new PathParseException($"Path segment count exceeds maximum ({MaxCommandCount})");
        budget.ChargeCommand();
    }

    private static List<PathToken> Tokenize(string pathData, PathOperationBudget budget)
    {
        if (pathData.Length > MaxPathDataCharacters)
            throw new PathParseException($"Path data exceeds maximum length ({MaxPathDataCharacters} characters)");
        budget.ChargeSourceCharacters(pathData.Length);

        var tokens = new List<PathToken>();
        var index = 0;
        var expectValueAfterComma = false;
        var command = '\0';
        var argumentIndex = 0;
        while (index < pathData.Length)
        {
            if ((index & 4095) == 0)
                budget.CheckCancellation();
            if (char.IsWhiteSpace(pathData[index]))
            {
                index++;
                continue;
            }

            if (pathData[index] == ',')
            {
                if (tokens.Count == 0 || expectValueAfterComma || tokens[^1].Command is not null)
                    throw new PathParseException($"Invalid comma at offset {index}");
                expectValueAfterComma = true;
                index++;
                continue;
            }

            var ch = pathData[index];
            // Arc flags are single characters, so "0110" can mean flags 0,1 and x=10.
            if (command is 'A' or 'a' && argumentIndex % 7 is 3 or 4)
            {
                if (ch is not ('0' or '1'))
                    throw new PathParseException($"Arc flag must be '0' or '1' at offset {index}");
                tokens.Add(new PathToken(null, ch - '0', index));
                argumentIndex++;
                expectValueAfterComma = false;
                index++;
                continue;
            }

            if (char.IsAsciiLetter(ch))
            {
                if (expectValueAfterComma)
                    throw new PathParseException($"Missing number after comma at offset {index}");
                if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(ch) < 0)
                    throw new PathParseException($"Unsupported SVG path command '{ch}' at offset {index}");
                tokens.Add(new PathToken(ch, null, index));
                command = ch;
                argumentIndex = 0;
                index++;
                continue;
            }

            var match = NumberPattern().Match(pathData, index);
            if (!match.Success)
                throw new PathParseException($"Invalid path data at offset {index}");
            if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                throw new PathParseException($"Non-finite number at offset {index}");
            if (Math.Abs(number) > MaxCoordinateMagnitude)
                throw new PathParseException($"Coordinate magnitude exceeds maximum ({MaxCoordinateMagnitude.ToString(CultureInfo.InvariantCulture)})");
            tokens.Add(new PathToken(null, number, index));
            argumentIndex++;
            expectValueAfterComma = false;
            index += match.Length;
        }

        if (expectValueAfterComma)
            throw new PathParseException("Path data ends after a comma");
        if (tokens.Count == 0)
            throw new PathParseException("Path contains no valid subpaths");
        return tokens;
    }

    private static void SetEndpoint(bool relative, double x, double y, ref double currentX, ref double currentY)
    {
        if (relative)
        {
            currentX += x;
            currentY += y;
        }
        else
        {
            currentX = x;
            currentY = y;
        }
    }

    private static void AddPoint(Path64 path, double x, double y, PathOperationBudget budget)
    {
        ValidateComputedCoordinate(x);
        ValidateComputedCoordinate(y);
        budget.ChargePoint();
        path.Add(new Point64(checked((long)Math.Round(x * Scale)), checked((long)Math.Round(y * Scale))));
    }

    private static void ValidateComputedCoordinate(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > MaxCoordinateMagnitude)
            throw new PathParseException("Computed coordinate is non-finite or exceeds the coordinate limit");
    }

    private static void AddCubic(
        Path64 path, double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, PathOperationBudget budget)
    {
        const int segments = 16;
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var u = 1 - t;
            var x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            var y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            AddPoint(path, x, y, budget);
        }
    }

    private static void AddArc(
        Path64 path, double x0, double y0, double rx, double ry, double rotation,
        bool largeArc, bool sweep, double x, double y, PathOperationBudget budget)
    {
        budget.CheckCancellation();
        ValidateComputedCoordinate(x);
        ValidateComputedCoordinate(y);
        if (x0 == x && y0 == y)
            return;
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx == 0 || ry == 0)
        {
            AddPoint(path, x, y, budget);
            return;
        }

        var (sin, cos) = Math.SinCos(rx == ry ? 0 : rotation % 360 * Math.PI / 180);
        var dx = x0 - x;
        var dy = y0 - y;
        var localX = cos * dx + sin * dy;
        var localY = -sin * dx + cos * dy;
        var normalizedX = localX / rx;
        var normalizedY = localY / ry;
        var distanceMantissa = double.Hypot(normalizedX, normalizedY);
        var distanceExponent = -1;
        var rxExponent = Math.ILogB(rx);
        var ryExponent = Math.ILogB(ry);

        // Recover direction separately from magnitude when the normalized chord under/overflows.
        // Power-of-two scaling preserves subnormal chords without collapsing an extreme radius ratio.
        if (!double.IsNormal(distanceMantissa) || double.IsSubnormal(Math.Max(Math.Abs(dx), Math.Abs(dy))))
        {
            var chordExponent = Math.ILogB(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            dx = Math.ScaleB(dx, -chordExponent);
            dy = Math.ScaleB(dy, -chordExponent);
            localX = cos * dx + sin * dy;
            localY = -sin * dx + cos * dy;
            var normalizationExponent = Math.Max(
                localX == 0 ? int.MinValue : Math.ILogB(localX) - rxExponent,
                localY == 0 ? int.MinValue : Math.ILogB(localY) - ryExponent);
            normalizedX = Math.ScaleB(localX, -rxExponent - normalizationExponent) / Math.ScaleB(rx, -rxExponent);
            normalizedY = Math.ScaleB(localY, -ryExponent - normalizationExponent) / Math.ScaleB(ry, -ryExponent);
            distanceMantissa = double.Hypot(normalizedX, normalizedY);
            distanceExponent += chordExponent + normalizationExponent;
        }

        var directionX = normalizedX / distanceMantissa;
        var directionY = normalizedY / distanceMantissa;
        var distance = Math.ScaleB(distanceMantissa, distanceExponent);
        // Do not enlarge radii for a few ulps of rotation/hypot roundoff; point limits stay strict.
        if (distance > 1 + 8 * (Math.BitIncrement(1) - 1))
        {
            var correctionExponent = Math.ILogB(distanceMantissa);
            var correctionMantissa = Math.ScaleB(distanceMantissa, -correctionExponent);
            rx = Math.ScaleB(Math.ScaleB(rx, -rxExponent) * correctionMantissa,
                rxExponent + distanceExponent + correctionExponent);
            ry = Math.ScaleB(Math.ScaleB(ry, -ryExponent) * correctionMantissa,
                ryExponent + distanceExponent + correctionExponent);
        }
        distance = Math.Min(1, distance);
        normalizedX = directionX * distance;
        normalizedY = directionY * distance;
        var centerFactor = (largeArc == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, (1 - distance) * (1 + distance)));
        var centerNormalizedX = centerFactor * directionY;
        var centerNormalizedY = -centerFactor * directionX;
        var centerX = cos * rx * centerNormalizedX - sin * ry * centerNormalizedY + (x0 + x) / 2;
        var centerY = sin * rx * centerNormalizedX + cos * ry * centerNormalizedY + (y0 + y) / 2;
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY))
            throw new PathParseException("Computed arc geometry is non-finite");

        var angle = Math.Atan2(normalizedY - centerNormalizedY, normalizedX - centerNormalizedX);
        var sweepAngle = 2 * Math.Asin(Math.Min(1, distance));
        if (largeArc)
            sweepAngle = 2 * Math.PI - sweepAngle;
        if (!sweep)
            sweepAngle = -sweepAngle;

        // At most four quarter-ellipse cubics, all using the existing flattening/point budget.
        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2)));
        var step = sweepAngle / segments;
        var controlScale = 4.0 / 3 * Math.Tan(step / 4);
        for (var i = 0; i < segments; i++)
        {
            var (startSin, startCos) = Math.SinCos(angle + i * step);
            var (endSin, endCos) = Math.SinCos(angle + (i + 1) * step);
            var endX = i == segments - 1 ? x : centerX + cos * rx * endCos - sin * ry * endSin;
            var endY = i == segments - 1 ? y : centerY + sin * rx * endCos + cos * ry * endSin;
            var x1 = x0 + controlScale * (-cos * rx * startSin - sin * ry * startCos);
            var y1 = y0 + controlScale * (-sin * rx * startSin + cos * ry * startCos);
            var x2 = endX - controlScale * (-cos * rx * endSin - sin * ry * endCos);
            var y2 = endY - controlScale * (-sin * rx * endSin + cos * ry * endCos);
            AddCubic(path, x0, y0, x1, y1, x2, y2, endX, endY, budget);
            x0 = endX;
            y0 = endY;
        }
    }

    private static void AddQuadratic(
        Path64 path, double x0, double y0, double x1, double y1,
        double x2, double y2, PathOperationBudget budget)
    {
        const int segments = 12;
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var u = 1 - t;
            var x = u * u * x0 + 2 * u * t * x1 + t * t * x2;
            var y = u * u * y0 + 2 * u * t * y1 + t * t * y2;
            AddPoint(path, x, y, budget);
        }
    }

    private static string PathsToSvgPath(Paths64 paths, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var outputPointCount = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.Count == 0)
                continue;
            if (outputPointCount > MaxOutputPointCount - path.Count)
                throw new PathParseException($"Path output point count exceeds maximum ({MaxOutputPointCount})");
            outputPointCount += path.Count;
            AppendOutput(sb, "M");
            AppendOutput(sb, FormatCoord(path[0].X / Scale));
            AppendOutput(sb, ",");
            AppendOutput(sb, FormatCoord(path[0].Y / Scale));
            for (var i = 1; i < path.Count; i++)
            {
                if ((i & 1023) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                AppendOutput(sb, "L");
                AppendOutput(sb, FormatCoord(path[i].X / Scale));
                AppendOutput(sb, ",");
                AppendOutput(sb, FormatCoord(path[i].Y / Scale));
            }
            AppendOutput(sb, "Z");
        }
        return sb.ToString();
    }

    private static void AppendOutput(StringBuilder builder, string value)
    {
        if (builder.Length > MaxOutputCharacters - value.Length)
            throw new PathParseException($"Path output exceeds maximum length ({MaxOutputCharacters} characters)");
        builder.Append(value);
    }

    private static string FormatCoord(double value)
    {
        var rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            rounded = 0;
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly record struct PathToken(char? Command, double? Number, int Offset);

    private sealed class PathOperationBudget(CancellationToken cancellationToken)
    {
        private int _sourceCharacters;
        private int _commandCount;
        private int _pointCount;

        public void ChargeSourceCharacters(int count)
        {
            CheckCancellation();
            if (count < 0 || _sourceCharacters > MaxAggregatePathDataCharacters - count)
            {
                throw new PathParseException(
                    $"Combined path data exceeds maximum length ({MaxAggregatePathDataCharacters} characters)");
            }
            _sourceCharacters += count;
        }

        public void ChargeCommand()
        {
            if (++_commandCount > MaxAggregateCommandCount)
            {
                throw new PathParseException(
                    $"Path segment count exceeds maximum ({MaxAggregateCommandCount})");
            }
            if ((_commandCount & 255) == 0)
                CheckCancellation();
        }

        public void ChargePoint()
        {
            if (++_pointCount > MaxFlattenedPointCount)
            {
                throw new PathParseException(
                    $"Combined flattened path complexity exceeds maximum ({MaxFlattenedPointCount} points)");
            }
            if ((_pointCount & 1023) == 0)
                CheckCancellation();
        }

        public void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed class PathParseException(string message) : Exception(message);
}

public enum OffsetJoinType
{
    Miter,
    Round,
    Square
}

public enum OffsetEndType
{
    Polygon,
    Joined,
    Butt,
    Square,
    Round
}
