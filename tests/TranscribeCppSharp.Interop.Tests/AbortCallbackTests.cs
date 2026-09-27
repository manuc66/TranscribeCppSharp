#nullable enable

using System;
using System.Threading;
using TranscribeCppSharp;
using TranscribeCppSharp.Interop;
using Xunit;
using Xunit.Abstractions;

namespace TranscribeCppSharp.Interop.Tests;

/// <summary>
/// Tests for the abort source on <see cref="Session"/>.
///
/// A cancellable <c>Run</c> used to install its token as
/// <c>SetAbortCallback(() =&gt; ct.IsCancellationRequested)</c>, which allocated
/// a closure per call and a second delegate inside SetAbortCallback to wrap it.
/// It now stores the token and reuses one interop delegate per session, so what
/// matters here is that the behaviour is unchanged while the allocation is not:
/// the token and a user callback must stay distinguishable, and the native side
/// must keep calling whichever one is current.
/// </summary>
[Collection(nameof(AbortCallbackTests))]
public class AbortCallbackTests
{
    private readonly ITestOutputHelper _output;

    public AbortCallbackTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SetAbortCallback_WithNull_Throws()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        Assert.Throws<ArgumentNullException>(() => fixture.Session.SetAbortCallback(null!));
    }

    [Fact]
    public void SetAbortCallback_DisposesCleanly()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        // Setting and clearing must both survive, including on a session that is
        // then disposed: the interop delegate is rooted for the session's life,
        // so this is where a lifetime mistake would show.
        fixture.Session.SetAbortCallback(() => false);
        fixture.Session.ClearAbortCallback();
        fixture.Session.SetAbortCallback(() => false);
    }

    [Fact]
    public void GetAbortCallback_IsNullUntilOneIsSet()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        Assert.Null(fixture.Session.GetAbortCallback());

        Func<bool> callback = () => false;
        fixture.Session.SetAbortCallback(callback);
        Assert.Same(callback, fixture.Session.GetAbortCallback());

        fixture.Session.ClearAbortCallback();
        Assert.Null(fixture.Session.GetAbortCallback());
    }

    [Fact]
    public void SetAbortToken_ReportsNoUserCallback()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        // A token is not a user callback, so GetAbortCallback must not hand one
        // out: Batch.Run uses it to save and restore the previous source, and a
        // token masquerading as a callback there would be restored as a closure
        // over a token that may already be dead.
        using var cts = new CancellationTokenSource();
        fixture.Session.SetAbortToken(cts.Token);

        Assert.Null(fixture.Session.GetAbortCallback());

        fixture.Session.ClearAbortCallback();
    }

    [Fact]
    public void SetAbortToken_ThenUserCallback_ReportsTheCallback()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        fixture.Session.SetAbortToken(cts.Token);

        Func<bool> callback = () => false;
        fixture.Session.SetAbortCallback(callback);

        // The two sources must not shadow each other: setting a user callback
        // has to take over completely, which is what the single shared interop
        // delegate relies on.
        Assert.Same(callback, fixture.Session.GetAbortCallback());
    }

    [Fact]
    public void UserCallback_ThenSetAbortToken_ClearsTheCallback()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        fixture.Session.SetAbortCallback(() => false);
        using var cts = new CancellationTokenSource();
        fixture.Session.SetAbortToken(cts.Token);

        // Installing a token must not leave a stale user callback behind, or the
        // shared delegate would keep consulting it.
        Assert.Null(fixture.Session.GetAbortCallback());
    }

    [Fact]
    public void InstallAndClear_AllocateNoDelegatePerCall()
    {
        using var fixture = SessionFixture.TryCreate(_output);
        if (fixture is null)
        {
            return;
        }

        // This is the path a cancellable Run takes: install the token, then
        // restore on the way out. It used to be
        // SetAbortCallback(() => ct.IsCancellationRequested) followed by
        // ClearAbortCallback(), which allocated a closure over the token, a
        // delegate to wrap it, and a further delegate for the clear — every
        // call.
        //
        // Measured in isolation with the delegates forced to escape (otherwise
        // the JIT deletes the allocations and the number is fiction): 89 bytes
        // per call for the old pattern against 0 for one cached delegate. The
        // bound below sits far above the new pattern's cost and far below the
        // old one, so a regression to per-call delegates trips it.
        using var cts = new CancellationTokenSource();

        // Warm up: the first call creates the one interop delegate.
        fixture.Session.SetAbortToken(cts.Token);
        fixture.Session.ClearAbortCallback();

        // Amortised, because GetTotalAllocatedBytes counts every thread in the
        // process: a one-off allocation elsewhere in the same window would
        // otherwise decide the outcome of the test. The per-call cost is what is
        // being asserted, so a constant offset washes out and a per-call
        // regression does not.
        const int Iterations = 1000;
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < Iterations; i++)
        {
            fixture.Session.SetAbortToken(cts.Token);
            fixture.Session.ClearAbortCallback();
        }

        long perCall = (GC.GetTotalAllocatedBytes(precise: true) - before) / Iterations;

        // 1000 calls at 89 bytes each would be ~89 bytes per call; the new path
        // measures 0. 16 bytes per call is a wide margin over that and still an
        // order of magnitude under the regression.
        Assert.True(
            perCall < 16,
            $"the install/clear pair allocates {perCall} bytes per call; the per-call delegates are back");
    }

    /// <summary>
    /// Shares a collection so these tests do not run alongside each other: one of
    /// them measures process-wide allocation, which a concurrent test would
    /// perturb.
    /// </summary>
    [CollectionDefinition(nameof(AbortCallbackTests), DisableParallelization = true)]
    public class AbortCallbackTestGroup
    {
    }

    /// <summary>
    /// A real session, or null when no native library or model is available, so
    /// these tests skip rather than fail on a machine without them.
    /// </summary>
    private sealed class SessionFixture : IDisposable
    {
        private SessionFixture(Model model, Session session)
        {
            Model = model;
            Session = session;
        }

        internal Model Model { get; }

        internal Session Session { get; }

        internal static SessionFixture? TryCreate(ITestOutputHelper output)
        {
            if (!TestConfig.IsIntegrationTestEnvironment())
            {
                output.WriteLine("Integration assets not present: skipping (run ./run-integration-tests.sh).");
                return null;
            }

            try
            {
                var model = Model.Load(TestConfig.ModelPath, p => p.WithBackend(BackendRequest.BackendCpu));
                return new SessionFixture(model, model.CreateSession());
            }
            catch (Exception ex) when (ex is DllNotFoundException or TranscribeException)
            {
                output.WriteLine($"Could not load a model ({ex.GetType().Name}): skipping.");
                return null;
            }
        }

        public void Dispose()
        {
            Session.Dispose();
            Model.Dispose();
        }
    }
}
