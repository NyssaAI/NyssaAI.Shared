namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// Ephemeral in-memory wrapper for a captured secret that scrubs its buffer on disposal.
/// </summary>
public sealed class CapturedSecret : IDisposable
{
    private readonly char[] _buffer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapturedSecret"/> class.
    /// </summary>
    /// <param name="buffer">Underlying mutable buffer of secret characters.</param>
    /// <param name="selectedProvider">The destination provider name chosen.</param>
    public CapturedSecret(char[] buffer, string selectedProvider)
    {
        _buffer = buffer;
        SelectedProvider = selectedProvider;
    }

    /// <summary>A span view over the secret characters.</summary>
    public ReadOnlySpan<char> ValueSpan => _buffer.AsSpan();

    /// <summary>String view over the secret characters.</summary>
    public string ValueString => new string(_buffer);

    /// <summary>The storage provider selected by the user or preferred default.</summary>
    public string SelectedProvider { get; }

    /// <summary>Zeros out the internal character buffer immediately upon disposal.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _disposed = true;
        }
    }
}
