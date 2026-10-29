using FTPlus.AgentGraphicDesign.Core.Documents.Nodes;
using Xunit;

namespace FTPlus.AgentGraphicDesign.Core.Tests.Engine;

public sealed class NodePageTests
{
    [Fact]
    public void ScanBudget_AllowsCompletePaginationAndRejectsOversizedTrees()
    {
        var engine = new DocumentEngine();
        var document = engine.CreateDocument("budget");
        Assert.Equal(100_000, NodePageQuery.MaxScannedNodes);
        for (var i = 0; i < 99_999; i++)
            document.Root.Children.Add(new GroupNode { Id = Guid.NewGuid(), Order = i.ToString("D6") });
        var last = engine.ListNodesPage(document, new NodePageQuery(Offset: 99_998, Limit: 2));
        Assert.Equal(100_000, last.Total);
        Assert.Equal(2, last.Nodes.Length);
        Assert.False(last.HasMore);
        Assert.Empty(engine.ListNodesPage(document, new NodePageQuery(Offset: NodePageQuery.MaxScannedNodes)).Nodes);
        document.Root.Children.Add(new GroupNode { Id = Guid.NewGuid(), Order = "z" });
        Assert.Equal("RESOURCE_LIMIT", Assert.Throws<DocumentEngineException>(() =>
            engine.ListNodesPage(document, new NodePageQuery(Limit: 1))).Code);
    }

    [Fact]
    public void Cancellation_IsObservedBeforeTraversal()
    {
        var engine = new DocumentEngine();
        var document = engine.CreateDocument("cancel");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => engine.ListNodesPage(document, new NodePageQuery(), cancellation.Token));
    }

    [Fact]
    public void SelectedSubtreeAndDepth_DoNotExpandUnrelatedOversizedContent()
    {
        var engine = new DocumentEngine();
        var document = engine.CreateDocument("selection");
        var selected = new GroupNode { Id = Guid.NewGuid(), Order = "a0" };
        selected.Children.Add(new RectangleNode { Id = Guid.NewGuid(), Order = "a0", Width = 1, Height = 1 });
        var unrelated = new GroupNode { Id = Guid.NewGuid(), Order = "a1" };
        for (var i = 0; i < 100_000; i++) unrelated.Children.Add(new GroupNode { Id = Guid.NewGuid(), Order = i.ToString("D6") });
        document.Root.Children.Add(selected);
        document.Root.Children.Add(unrelated);
        var page = engine.ListNodesPage(document, new NodePageQuery(selected.Id));
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { 0, 1 }, page.Nodes.Select(node => node.Depth));
        Assert.Single(engine.ListNodesPage(document, new NodePageQuery(MaxDepth: 0)).Nodes);
        Assert.Equal("RESOURCE_LIMIT", Assert.Throws<DocumentEngineException>(() => engine.ListNodesPage(document, new NodePageQuery())).Code);
    }

    [Fact]
    public async Task Cancellation_DuringBroadTraversalIsObserved()
    {
        var engine = new DocumentEngine();
        var document = engine.CreateDocument("active-cancel");
        for (var i = 0; i < 99_999; i++) document.Root.Children.Add(new GroupNode { Id = Guid.NewGuid(), Order = (99_999 - i).ToString("D6") });
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var visited = 0;
        var task = Task.Run(() => engine.ListNodesPage(document, new NodePageQuery(), cancellation.Token, count =>
        {
            visited = count;
            if (count != 128) return;
            Assert.False(cancellation.IsCancellationRequested);
            started.SetResult();
            Assert.True(resume.Wait(TimeSpan.FromSeconds(5)), "Cancellation coordinator did not release traversal");
        }));
        Exception? coordinationError = null;
        try
        {
            coordinationError = await Record.ExceptionAsync(() => started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            cancellation.Cancel();
            resume.Set();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.Null(coordinationError);
        Assert.Equal(128, visited);
    }
}
