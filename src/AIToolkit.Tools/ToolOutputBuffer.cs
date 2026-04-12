using System.Text;

namespace AIToolkit.Tools;

/// <summary>
/// Keeps captured process output bounded for foreground and background tool execution.
/// </summary>
internal sealed class ToolOutputBuffer(int maxCharacters)
{
    private readonly int _maxCharacters = Math.Max(1, maxCharacters);
    private readonly StringBuilder _builder = new();

    public bool Truncated { get; private set; }

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

    public override string ToString() => _builder.ToString();
}