namespace FTPlus.AgentGraphicDesign.Core.Pixels;

/// <summary>
/// One admitted native pixel operation. Owns the host-lifetime shared slot (a single
/// concurrent heavy native call per host), the whole-operation cooperative deadline, and
/// the working-reservation accounting defined by <c>agd.pixels.runtime.v1</c>. Construction
/// of <see cref="NativePixelService"/> is I/O-free; admission happens here, per call.
/// </summary>
internal sealed class NativeOperationScope : IDisposable
{
    // 0 = free, 1 = a native operation currently holds the single host slot.
    private static int _hostSlot;

    internal const long MaxReservationBytes = 268_435_456; // 256 MiB working reservation.
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(60);

    private readonly CancellationToken _caller;
    private readonly CancellationTokenSource _deadline;
    private long _reserved;
    private bool _slotHeld;
    private bool _disposed;

    private NativeOperationScope(CancellationToken caller, TimeSpan deadline)
    {
        _caller = caller;
        _deadline = new CancellationTokenSource(deadline);
    }

    /// <summary>
    /// Admits a heavy native call. Already-requested caller cancellation is honored first;
    /// otherwise a competing call fails immediately with <c>PIXEL_BUSY</c> and the fixed
    /// slot-busy message, with no queue.
    /// </summary>
    internal static NativeOperationScope Enter(CancellationToken caller) => Enter(caller, DefaultDeadline);

    internal static NativeOperationScope Enter(CancellationToken caller, TimeSpan deadline)
    {
        caller.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _hostSlot, 1, 0) != 0)
            throw new NativePixelException(NativePixelErrors.PixelBusy, NativePixelErrors.SlotBusyMessage);

        var scope = new NativeOperationScope(caller, deadline);
        scope._slotHeld = true;
        return scope;
    }

    /// <summary>
    /// Storage-layer test support only: a scope that does NOT take the single host slot, so the
    /// per-asset cross-process writer lock can be exercised by concurrent in-process callers.
    /// Production entry points always go through <see cref="Enter(CancellationToken)"/>.
    /// </summary>
    internal static NativeOperationScope EnterUnadmitted(CancellationToken caller, TimeSpan? deadline = null) =>
        new(caller, deadline ?? DefaultDeadline);

    /// <summary>The caller's own token, preserved so cancellation surfaces with it.</summary>
    internal CancellationToken Caller => _caller;

    /// <summary>Method-group target for the format/renderer <c>checkBudget</c> parameters.</summary>
    internal Action Budget => Check;

    /// <summary>
    /// Cooperative checkpoint. Caller cancellation takes precedence and propagates as
    /// <see cref="OperationCanceledException"/>; deadline exhaustion is <c>RESOURCE_LIMIT</c>.
    /// </summary>
    internal void Check()
    {
        _caller.ThrowIfCancellationRequested();
        if (_deadline.IsCancellationRequested)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Native operation deadline was exceeded.");
    }

    /// <summary>Charges conservative working bytes; exceeding 256 MiB is <c>RESOURCE_LIMIT</c>.</summary>
    internal void Reserve(long bytes)
    {
        if (bytes <= 0) return;
        _reserved = checked(_reserved + bytes);
        if (_reserved > MaxReservationBytes)
            throw new NativePixelException(EngineErrors.ResourceLimit, "Native working reservation was exceeded.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_slotHeld)
        {
            Interlocked.Exchange(ref _hostSlot, 0);
            _slotHeld = false;
        }
        _deadline.Dispose();
    }
}
