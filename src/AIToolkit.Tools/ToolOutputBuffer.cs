using System.Text;

namespace AIToolkit.Tools;

/// <summary>
/// Keeps captured process output bounded for foreground and background tool execution.
/// </summary>
/// <remarks>
/// Output is truncated once the configured limit is reached so long-running commands cannot grow memory without
/// bound. Callers can inspect <see cref="Truncated"/> to decide whether the returned output is complete.
/// </remarks>
/// <param name="maxCharacters">The maximum number of characters retained in the buffer.</param>
internal sealed class ToolOutputBuffer(int maxCharacters)
{
    private readonly int _maxCharacters = Math.Max(1, maxCharacters);
    private readonly StringBuilder _builder = new();

    /// <summary>
    /// Gets a value indicating whether text was discarded because the buffer reached its configured limit.
    /// </summary>
    public bool Truncated { get; private set; }

    /// <summary>
    /// Appends text to the buffer until the configured character limit is reached.
    /// </summary>
    /// <param name="value">The text to append.</param>
    public void Append(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (_builder.Length >= _maxCharacters)
        {
            Truncated = true;
            return;
        }

        var remaining = _maxCharacters - _builder.Length;
        if (value.Length <= remaining)
        {
            _builder.Append(value);
            return;
        }

        _builder.Append(value.AsSpan(0, remaining));
        Truncated = true;
    }

    /// <summary>
    /// Returns the buffered output accumulated so far.
    /// </summary>
    /// <returns>The buffered text.</returns>
    public override string ToString() => _builder.ToString();
}
