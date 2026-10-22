using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FTPlus.AgentGraphicDesign.Core.Artifacts;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Schema;

public class ProvidedArtifactSchemaTests
{
    [Fact]
    public void Schema_DeclaresDraftAndStableNonPublicIdentity()
    {
        using var schema = LoadSchema();
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("urn:agd:provided-artifact:1", schema.RootElement.GetProperty("$id").GetString());
    }

    [Theory]
    [InlineData("valid-still.json")]
    [InlineData("valid-clip.json")]
    public void SharedRawFixtures_SatisfySchemaAndMetadataParser(string fixture)
    {
        var bytes = ReadFixture(fixture);
        Assert.Empty(ValidateSchema(bytes));
        Assert.True(ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Theory]
    [InlineData("valid-still.json", "", "schema,version,profile,assetId,canvas,cropOrigin,files,still")]
    [InlineData("valid-clip.json", "", "schema,version,profile,assetId,canvas,cropOrigin,files,clip")]
    [InlineData("valid-still.json", "canvas", "width,height,coordinates")]
    [InlineData("valid-still.json", "cropOrigin", "x,y")]
    [InlineData("valid-still.json", "files.0", "sha256,path,byteLength")]
    [InlineData("valid-still.json", "still", "name,fileSha256,pivot")]
    [InlineData("valid-still.json", "still.pivot", "x,y")]
    [InlineData("valid-clip.json", "clip", "id,viewId,variantId,ticksPerSecond,playback,frames")]
    [InlineData("valid-clip.json", "clip.frames.0", "name,fileSha256,durationTicks,pivot")]
    [InlineData("valid-clip.json", "clip.frames.0.pivot", "x,y")]
    [InlineData("valid-still.json", "provenance.producer", "id,version")]
    public void EveryRequiredField_RejectsAbsenceAndExplicitNull(string fixture, string objectPath, string fields)
    {
        foreach (var field in fields.Split(','))
        {
            var document = JsonNode.Parse(ReadFixture(fixture))!;
            var target = At(document, objectPath).AsObject();
            Assert.True(target.Remove(field));
            Assert.NotEmpty(ValidateSchema(Encoding.UTF8.GetBytes(document.ToJsonString())));
            target[field] = null;
            Assert.NotEmpty(ValidateSchema(Encoding.UTF8.GetBytes(document.ToJsonString())));
        }
    }

    [Theory]
    [InlineData("valid-still.json", "")]
    [InlineData("valid-still.json", "canvas")]
    [InlineData("valid-still.json", "cropOrigin")]
    [InlineData("valid-still.json", "files.0")]
    [InlineData("valid-still.json", "still")]
    [InlineData("valid-still.json", "still.pivot")]
    [InlineData("valid-clip.json", "clip")]
    [InlineData("valid-clip.json", "clip.frames.0")]
    [InlineData("valid-clip.json", "clip.frames.0.pivot")]
    [InlineData("valid-still.json", "provenance")]
    [InlineData("valid-still.json", "provenance.producer")]
    public void EveryObject_IsClosed(string fixture, string objectPath)
    {
        var bytes = Mutate(fixture, document => At(document, objectPath)["admitted"] = true);
        Assert.NotEmpty(ValidateSchema(bytes));
        Assert.False(ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Theory]
    [InlineData("valid-still.json", "", "schema", "\"agd.document\"")]
    [InlineData("valid-still.json", "", "version", "2")]
    [InlineData("valid-still.json", "", "version", "\"1\"")]
    [InlineData("valid-still.json", "", "profile", "\"png8-rgba-srgb-premultiplied-v1\"")]
    [InlineData("valid-still.json", "canvas", "coordinates", "\"bottom-left\"")]
    [InlineData("valid-still.json", "cropOrigin", "x", "1")]
    [InlineData("valid-still.json", "cropOrigin", "y", "-1")]
    [InlineData("valid-still.json", "canvas", "width", "0")]
    [InlineData("valid-still.json", "canvas", "width", "8193")]
    [InlineData("valid-still.json", "canvas", "height", "0")]
    [InlineData("valid-still.json", "canvas", "height", "8193")]
    [InlineData("valid-still.json", "canvas", "width", "1.5")]
    [InlineData("valid-still.json", "files.0", "byteLength", "0")]
    [InlineData("valid-still.json", "files.0", "byteLength", "67108865")]
    [InlineData("valid-still.json", "files.0", "byteLength", "1.5")]
    [InlineData("valid-still.json", "", "assetId", "\"Bad\"")]
    [InlineData("valid-still.json", "still", "name", "\"0bad\"")]
    [InlineData("valid-clip.json", "clip", "id", "\"bad id\"")]
    [InlineData("valid-clip.json", "clip", "viewId", "\"bad.id\"")]
    [InlineData("valid-clip.json", "clip", "variantId", "\"\"")]
    [InlineData("valid-clip.json", "clip.frames.0", "name", "\"Bad\"")]
    [InlineData("valid-still.json", "provenance.producer", "id", "\"Bad\"")]
    [InlineData("valid-still.json", "provenance.producer", "version", "\"+bad\"")]
    [InlineData("valid-still.json", "files.0", "sha256", "\"abc\"")]
    [InlineData("valid-still.json", "still", "fileSha256", "\"abc\"")]
    [InlineData("valid-clip.json", "clip.frames.0", "fileSha256", "\"abc\"")]
    [InlineData("valid-still.json", "provenance", "recipeSnapshotSha256", "\"abc\"")]
    [InlineData("valid-clip.json", "clip", "ticksPerSecond", "0")]
    [InlineData("valid-clip.json", "clip", "ticksPerSecond", "1000000001")]
    [InlineData("valid-clip.json", "clip", "ticksPerSecond", "1.5")]
    [InlineData("valid-clip.json", "clip", "playback", "\"ping-pong\"")]
    [InlineData("valid-clip.json", "clip.frames.0", "durationTicks", "0")]
    [InlineData("valid-clip.json", "clip.frames.0", "durationTicks", "9007199254740992")]
    [InlineData("valid-clip.json", "clip.frames.0", "durationTicks", "1.5")]
    [InlineData("valid-still.json", "still.pivot", "x", "-1000000.000001")]
    [InlineData("valid-still.json", "still.pivot", "y", "1000000.000001")]
    [InlineData("valid-still.json", "still.pivot", "x", "0.0000001")]
    [InlineData("valid-clip.json", "clip.frames.0.pivot", "x", "1000000.000001")]
    [InlineData("valid-clip.json", "clip.frames.0.pivot", "y", "0.0000001")]
    [InlineData("valid-still.json", "", "canvas", "[]")]
    [InlineData("valid-still.json", "", "files", "{}")]
    [InlineData("valid-still.json", "", "files", "[null]")]
    [InlineData("valid-still.json", "still", "durationTicks", "1")]
    [InlineData("valid-clip.json", "clip", "frames", "[null]")]
    public void InvalidShapeAndValues_AreRejectedBySchemaAndParser(string fixture, string objectPath, string field, string raw)
    {
        var bytes = Mutate(fixture, document => At(document, objectPath)[field] = JsonNode.Parse(raw));
        Assert.NotEmpty(ValidateSchema(bytes));
        Assert.False(ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StillAndClip_AreMutuallyExclusiveEvenWithMultipleFiles(bool multipleFiles)
    {
        var bytes = Mutate("valid-clip.json", document =>
        {
            document["still"] = JsonNode.Parse(ReadFixture("valid-still.json"))!["still"]!.DeepClone();
            if (!multipleFiles) document["files"]!.AsArray().RemoveAt(0);
        });
        Assert.NotEmpty(ValidateSchema(bytes));
    }

    [Theory]
    [InlineData("valid-still.json", "files", 0, false)]
    [InlineData("valid-still.json", "files", 1, true)]
    [InlineData("valid-still.json", "files", 2, false)]
    [InlineData("valid-clip.json", "files", 0, false)]
    [InlineData("valid-clip.json", "files", 1, true)]
    [InlineData("valid-clip.json", "files", 4096, true)]
    [InlineData("valid-clip.json", "files", 4097, false)]
    [InlineData("valid-clip.json", "clip.frames", 0, false)]
    [InlineData("valid-clip.json", "clip.frames", 1, true)]
    [InlineData("valid-clip.json", "clip.frames", 4096, true)]
    [InlineData("valid-clip.json", "clip.frames", 4097, false)]
    public void ArrayCounts_AreStructuralNotUniquenessOrAggregateChecks(string fixture, string arrayPath, int count, bool valid)
    {
        var bytes = Mutate(fixture, document =>
        {
            var array = At(document, arrayPath).AsArray();
            var item = array[0]!.DeepClone();
            array.Clear();
            for (var i = 0; i < count; i++) array.Add(item.DeepClone());
        });
        Assert.Equal(valid, ValidateSchema(bytes).Count == 0);
    }

    [Theory]
    [InlineData("absent", true)]
    [InlineData("producer", true)]
    [InlineData("recipeSnapshotSha256", true)]
    [InlineData("both", true)]
    [InlineData("empty", false)]
    [InlineData("null", false)]
    [InlineData("null-producer", false)]
    [InlineData("null-recipeSnapshotSha256", false)]
    public void Provenance_IsOptionalButNonemptyAndNeverNull(string mode, bool valid)
    {
        var bytes = Mutate("valid-still.json", document =>
        {
            var provenance = document["provenance"]!.AsObject();
            switch (mode)
            {
                case "absent": document.AsObject().Remove("provenance"); break;
                case "producer": provenance.Remove("recipeSnapshotSha256"); break;
                case "recipeSnapshotSha256": provenance.Remove("producer"); break;
                case "empty": provenance.Clear(); break;
                case "null": document["provenance"] = null; break;
                case "null-producer": provenance["producer"] = null; break;
                case "null-recipeSnapshotSha256": provenance["recipeSnapshotSha256"] = null; break;
            }
        });
        Assert.Equal(valid, ValidateSchema(bytes).Count == 0);
        Assert.Equal(valid, ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("0/a.b_c-d.png", true)]
    [InlineData("a..b/frame.png", true)]
    [InlineData("/a.png", false)]
    [InlineData("a//b.png", false)]
    [InlineData("./a.png", false)]
    [InlineData("../a.png", false)]
    [InlineData("a/../b.png", false)]
    [InlineData("a./b.png", false)]
    [InlineData("a\\b.png", false)]
    [InlineData("c:/a.png", false)]
    [InlineData("https://example/a.png", false)]
    [InlineData("a%20b.png", false)]
    [InlineData("a b.png", false)]
    [InlineData("A.png", false)]
    [InlineData("a.PNG", false)]
    [InlineData("\u00e9.png", false)]
    [InlineData("_a.png", false)]
    [InlineData(".png", false)]
    [InlineData("a.jpg", false)]
    [InlineData("a.png.", false)]
    [InlineData("a.png/", false)]
    [InlineData("a.png\n", false)]
    public void Paths_EnforcePortableAsciiSlashPngBasics(string path, bool valid)
    {
        var bytes = Mutate("valid-still.json", document => document["files"]![0]!["path"] = path);
        Assert.Equal(valid, ValidateSchema(bytes).Count == 0);
        Assert.Equal(valid, ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Fact]
    public void StringAndPathLengthBoundaries_AreInclusive()
    {
        using var schema = LoadSchema();
        var cases = new (string Definition, string Value, bool Valid)[]
        {
            ("logicalId", "a", true), ("logicalId", new string('a', 64), true),
            ("logicalId", "", false), ("logicalId", new string('a', 65), false),
            ("logicalId", "a\n", false), ("logicalId", "\u00e9", false),
            ("sha256", new string('a', 64), true), ("sha256", new string('f', 63), false),
            ("sha256", new string('f', 65), false), ("sha256", new string('A', 64), false),
            ("sha256", new string('g', 64), false),
            ("path", new string('a', 60) + ".png", true), ("path", new string('a', 61) + ".png", false),
            ("path", new string('a', 64) + "/a.png", true), ("path", new string('a', 65) + "/a.png", false),
            ("path", string.Concat(Enumerable.Repeat("a/", 7)) + "a.png", true),
            ("path", string.Concat(Enumerable.Repeat("a/", 8)) + "a.png", false),
            ("path", string.Concat(Enumerable.Repeat(new string('a', 58) + "/", 4)) + "a.png", false),
            ("path", string.Concat(Enumerable.Repeat(new string('a', 58) + "/", 3)) + new string('a', 59) + ".png", true)
        };
        foreach (var (definition, value, valid) in cases)
        {
            using var instance = JsonDocument.Parse(JsonSerializer.Serialize(value));
            var errors = SchemaTestSupport.Validate(instance.RootElement,
                schema.RootElement.GetProperty("$defs").GetProperty(definition), schema.RootElement, "$");
            Assert.True(valid == (errors.Count == 0), $"{definition}: {value}");
        }

        foreach (var (version, valid) in new[] { ("A1.0+_-", true), (new string('A', 64), true), (new string('A', 65), false), ("", false), ("a\n", false) })
        {
            var bytes = Mutate("valid-still.json", document => document["provenance"]!["producer"]!["version"] = version);
            Assert.Equal(valid, ValidateSchema(bytes).Count == 0);
        }
    }

    [Theory]
    [InlineData("valid-still.json", "canvas", "width", "1")]
    [InlineData("valid-still.json", "canvas", "width", "8192")]
    [InlineData("valid-still.json", "canvas", "height", "8192")]
    [InlineData("valid-still.json", "files.0", "byteLength", "1")]
    [InlineData("valid-still.json", "files.0", "byteLength", "67108864")]
    [InlineData("valid-still.json", "still.pivot", "x", "-1000000")]
    [InlineData("valid-still.json", "still.pivot", "y", "1000000")]
    [InlineData("valid-still.json", "still.pivot", "x", "0.000001")]
    [InlineData("valid-clip.json", "clip", "ticksPerSecond", "1")]
    [InlineData("valid-clip.json", "clip", "ticksPerSecond", "1000000000")]
    [InlineData("valid-clip.json", "clip", "playback", "\"forward-loop\"")]
    [InlineData("valid-clip.json", "clip.frames.0", "durationTicks", "1")]
    [InlineData("valid-clip.json", "clip.frames.0", "durationTicks", "9007199254740991")]
    public void ValueBoundaries_DoNotImplyAggregateValidity(string fixture, string objectPath, string field, string raw)
    {
        var bytes = Mutate(fixture, document => At(document, objectPath)[field] = JsonNode.Parse(raw));
        Assert.Empty(ValidateSchema(bytes));
    }

    [Theory]
    [InlineData("", "version", "1.0")]
    [InlineData("", "version", "1e0")]
    [InlineData("cropOrigin", "x", "-0")]
    [InlineData("cropOrigin", "y", "0.0")]
    [InlineData("canvas", "width", "8.0")]
    [InlineData("files.0", "byteLength", "123e0")]
    [InlineData("still.pivot", "x", "1e-6")]
    [InlineData("still.pivot", "x", "4.0000000")]
    [InlineData("still.pivot", "x", "-0.0000000")]
    public void MathematicalValues_PassSchemaButInvalidTokenSpellingsFailParser(string objectPath, string field, string raw)
    {
        var bytes = Mutate("valid-still.json", document => At(document, objectPath)[field] = JsonNode.Parse(raw));
        Assert.Equal(raw, At(JsonNode.Parse(bytes)!, objectPath)[field]!.ToJsonString());
        Assert.Empty(ValidateSchema(bytes));
        Assert.False(ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Theory]
    [InlineData("missing-reference")]
    [InlineData("unreferenced-file")]
    [InlineData("duplicate-hash")]
    [InlineData("duplicate-path")]
    [InlineData("duplicate-frame-name")]
    [InlineData("path-prefix-conflict")]
    [InlineData("device-leaf")]
    [InlineData("device-directory")]
    [InlineData("canvas-area")]
    [InlineData("total-bytes")]
    [InlineData("unique-pixel-work")]
    [InlineData("logical-pixel-work")]
    [InlineData("total-duration")]
    public void RelationshipsAndAggregates_AreParserChecksNotSchemaParity(string violation)
    {
        var bytes = Mutate("valid-clip.json", document =>
        {
            var files = document["files"]!.AsArray();
            var frames = document["clip"]!["frames"]!.AsArray();
            switch (violation)
            {
                case "missing-reference": frames[0]!["fileSha256"] = new string('d', 64); break;
                case "unreferenced-file":
                    var unused = files[0]!.DeepClone();
                    unused["sha256"] = new string('d', 64);
                    unused["path"] = "unused.png";
                    files.Add(unused);
                    break;
                case "duplicate-hash":
                    var duplicate = files[1]!.DeepClone();
                    duplicate["path"] = "pixels/frame-a-copy.png";
                    files.Add(duplicate);
                    break;
                case "duplicate-path": files[1]!["path"] = files[0]!["path"]!.DeepClone(); break;
                case "duplicate-frame-name": frames[1]!["name"] = frames[0]!["name"]!.DeepClone(); break;
                case "path-prefix-conflict": files[0]!["path"] = "a.png"; files[1]!["path"] = "a.png/b.png"; break;
                case "device-leaf": files[0]!["path"] = "con.extra.png"; break;
                case "device-directory": files[0]!["path"] = "aux.dir/frame.png"; break;
                case "canvas-area": document["canvas"]!["width"] = 8192; document["canvas"]!["height"] = 8192; break;
                case "total-duration": frames[0]!["durationTicks"] = 9007199254740991L; break;
                case "total-bytes":
                case "unique-pixel-work":
                case "logical-pixel-work":
                    files.Clear();
                    frames.Clear();
                    document["canvas"]!["width"] = 4096;
                    document["canvas"]!["height"] = 4096;
                    var count = violation == "logical-pixel-work" ? 17 : 5;
                    for (var i = 0; i < count; i++)
                    {
                        var hash = i.ToString("x64", CultureInfo.InvariantCulture);
                        if (violation != "logical-pixel-work" || i == 0)
                            files.Add(new JsonObject { ["sha256"] = hash, ["path"] = $"frame-{i}.png", ["byteLength"] = violation == "total-bytes" ? 67108864 : 1 });
                        frames.Add(new JsonObject
                        {
                            ["name"] = $"frame-{i}",
                            ["fileSha256"] = violation == "logical-pixel-work" ? new string('0', 64) : hash,
                            ["durationTicks"] = 1,
                            ["pivot"] = new JsonObject { ["x"] = 0, ["y"] = 0 }
                        });
                    }
                    if (violation == "total-bytes")
                    {
                        document["canvas"]!["width"] = 1;
                        document["canvas"]!["height"] = 1;
                    }
                    break;
            }
        });
        Assert.Empty(ValidateSchema(bytes));
        Assert.False(ArtifactDescriptorContract.Parse(bytes).Success);
    }

    [Fact]
    public void DuplicateKeysAndInputByteLimits_AreNotSchemaAssertions()
    {
        var json = Encoding.UTF8.GetString(ReadFixture("valid-still.json"));
        var duplicate = Encoding.UTF8.GetBytes(json.Insert(json.IndexOf('{') + 1, "\"vers\\u0069on\":1,"));
        Assert.Empty(ValidateSchema(duplicate));
        Assert.False(ArtifactDescriptorContract.Parse(duplicate).Success);

        var oversized = Encoding.UTF8.GetBytes(new string(' ', 1048576) + json);
        Assert.Empty(ValidateSchema(oversized));
        Assert.False(ArtifactDescriptorContract.Parse(oversized).Success);
    }

    private static JsonDocument LoadSchema() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
        JsonSchemaContractTests.FindRepositoryRoot(), "schemas", "agd-provided-artifact-v1.schema.json")));

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(
        JsonSchemaContractTests.FindRepositoryRoot(), "tests", "FTPlus.AgentGraphicDesign.Core.Tests", "Artifacts", "Fixtures", name));

    private static List<string> ValidateSchema(byte[] bytes)
    {
        using var schema = LoadSchema();
        using var instance = JsonDocument.Parse(bytes);
        return SchemaTestSupport.Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$");
    }

    private static byte[] Mutate(string fixture, Action<JsonNode> mutate)
    {
        var document = JsonNode.Parse(ReadFixture(fixture))!;
        mutate(document);
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    private static JsonNode At(JsonNode document, string path)
    {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            document = document is JsonArray ? document[int.Parse(segment, CultureInfo.InvariantCulture)]! : document[segment]!;
        return document;
    }
}
