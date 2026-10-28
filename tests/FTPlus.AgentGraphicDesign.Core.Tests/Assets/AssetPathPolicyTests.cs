using FTPlus.AgentGraphicDesign.Core.Assets;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Assets;

public sealed class AssetPathPolicyTests
{
    [Theory]
    [InlineData("caf\u00e9.svg", "cafe\u0301.svg")]
    [InlineData("cafe\u0301.svg", "CAF\u00c9.svg")]
    [InlineData("/root/caf\u00e9/x.svg", "/root/cafe\u0301/x.svg")]
    public void CanonicallyEquivalentKeys_CollideWithoutRewritingTheOperand(string first, string second)
    {
        Assert.True(AssetPathPolicy.MacComparer.Equals(first, second));
        Assert.Equal(AssetPathPolicy.MacComparer.GetHashCode(first), AssetPathPolicy.MacComparer.GetHashCode(second));
        var paths = new HashSet<string>(AssetPathPolicy.MacComparer) { first };
        Assert.False(paths.Add(second));
        Assert.Equal(first, Assert.Single(paths));
    }

    [Theory]
    [InlineData("caf\u00e9", "cafe\u0301")]
    [InlineData("cafe\u0301", "caf\u00e9")]
    [InlineData("OUTPUT", "output")]
    public void ExistingAlias_IsRejectedEvenIfRequestedSpellingDoesNotExist(string stored, string requested)
    {
        var path = new AssetPathPolicy(true, _ => ["/" + stored], _ => false, (_, _) => { });
        Assert.Equal(EngineErrors.PathEscape,
            Assert.Throws<DocumentEngineException>(() => path.Check("/" + requested + "/new.svg", default)).Code);
    }

    [Fact]
    public void ExistingUnknownFilesystemEquivalence_StillRequiresExactName()
    {
        var path = new AssetPathPolicy(true, _ => ["/different"], _ => true, (_, _) => { });
        Assert.Equal(EngineErrors.PathEscape,
            Assert.Throws<DocumentEngineException>(() => path.Check("/requested", default)).Code);
    }

    [Fact]
    public void ExactDecomposedNamesAndMissingParents_AreAccepted()
    {
        var enumerated = new List<string>();
        var path = new AssetPathPolicy(true, parent =>
        {
            enumerated.Add(parent);
            return parent == "/" ? ["/cafe\u0301"] : [];
        }, _ => false, (_, _) => { });
        path.Check("/cafe\u0301/new/sub.svg", default);
        Assert.Equal(["/", "/cafe\u0301"], enumerated);
    }

    [Fact]
    public void CreatedEntries_RequireFreshInventoryWithoutResettingTheBudget()
    {
        string[] stored = [];
        var calls = 0;
        var path = new AssetPathPolicy(true, _ => { calls++; return stored; }, _ => false, (_, _) => { });
        path.Check("/result", default);
        path.Check("/another", default);
        Assert.Equal(1, calls);
        stored = ["/RESULT"];
        path.Invalidate();
        Assert.Equal(EngineErrors.PathEscape,
            Assert.Throws<DocumentEngineException>(() => path.Check("/result", default)).Code);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ParentAndAggregateEnumeration_AreBounded()
    {
        var oversized = new AssetPathPolicy(true, _ => Enumerable.Range(0, 4097).Select(i => "/f" + i), _ => false, (_, _) => { });
        Assert.Equal(EngineErrors.ResourceLimit,
            Assert.Throws<DocumentEngineException>(() => oversized.Check("/new", default)).Code);
        var total = new AssetPathPolicy(true, _ => Enumerable.Range(0, 4096).Select(i => "/f" + i), _ => false, (_, _) => { });
        for (var i = 0; i < 16; i++)
        {
            total.Check("/new", default);
            total.Invalidate();
        }
        Assert.Equal(EngineErrors.ResourceLimit,
            Assert.Throws<DocumentEngineException>(() => total.Check("/new", default)).Code);
    }

    [Fact]
    public void CancellationAndEnumerationFailure_NeverBecomeCachedSuccess()
    {
        using var cts = new CancellationTokenSource();
        IEnumerable<string> Entries(string _)
        {
            cts.Cancel();
            yield return "/first";
        }
        var cancelled = new AssetPathPolicy(true, Entries, _ => false, (_, _) => { });
        Assert.Throws<OperationCanceledException>(() => cancelled.Check("/new", cts.Token));
        var calls = 0;
        var failed = new AssetPathPolicy(true, _ => { calls++; throw new IOException("enumeration failed"); }, _ => false, (_, _) => { });
        Assert.Throws<IOException>(() => failed.Check("/new", default));
        Assert.Throws<IOException>(() => failed.Check("/new", default));
        Assert.Equal(2, calls);
    }
}
