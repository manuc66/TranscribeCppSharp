namespace TranscribeCppSharp;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Common base for the fluent <c>*ExtBuilder</c> types: owns the native
/// extension buffer, exposes <see cref="ThrowIfDisposed"/> / <see cref="Build"/>
/// / <see cref="Dispose"/>, and lets each derived builder add its own fields
/// and <c>With*</c> methods.
/// </summary>
/// <typeparam name="TParams">The native extension struct held by the buffer.</typeparam>
public abstract class ExtBuilder<TParams> : IDisposable
    where TParams : struct
{
    private readonly ExtBuffer<TParams> buffer;

    /// <summary>Creates the builder around the given native init function.</summary>
    protected ExtBuilder(
        Action<IntPtr> init,
        Func<TParams, ulong> sizeSelector,
        string builderName)
    {
        buffer = new ExtBuffer<TParams>(init, sizeSelector, builderName);
    }

    /// <summary>The native extension parameters, for the derived builder's <c>With*</c> methods.</summary>
    protected ref TParams Params => ref buffer.Params;

    /// <summary>Throws if the builder has been disposed.</summary>
    protected void ThrowIfDisposed() => buffer.ThrowIfDisposed();

    /// <summary>Builds the native extension pointer.</summary>
    protected internal IntPtr Build() => buffer.Build();

    /// <summary>
    /// Releases native resources owned by the derived builder (pointers, etc.).
    /// Called before the buffer itself is disposed.
    /// </summary>
    protected virtual void DisposeNativeResources()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeNativeResources();
        buffer.Dispose();
        GC.SuppressFinalize(this);
    }
}
