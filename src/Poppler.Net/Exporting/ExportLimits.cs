using System.Text;

namespace Poppler.Exporting;

/// <summary>
/// UTF-8-aware builder used by textual exports. Every append is charged
/// before the underlying <see cref="StringBuilder"/> is allowed to grow.
/// </summary>
internal sealed class BoundedUtf8Builder
{
    private readonly StringBuilder _builder = new();
    private readonly long _maximumBytes;
    private readonly string _diagnostic;
    private long _bytes;

    public BoundedUtf8Builder(long maximumBytes, string diagnostic)
    {
        if (maximumBytes < 1 || maximumBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        _maximumBytes = maximumBytes;
        _diagnostic = diagnostic;
    }

    public BoundedUtf8Builder Append(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return this;
        Reserve(Encoding.UTF8.GetByteCount(value));
        _builder.Append(value);
        return this;
    }

    public BoundedUtf8Builder Append(char value)
    {
        Reserve(value <= 0x7f ? 1 : Encoding.UTF8.GetByteCount(value.ToString()));
        _builder.Append(value);
        return this;
    }

    public BoundedUtf8Builder AppendLine()
    {
        Reserve(1);
        _builder.Append('\n');
        return this;
    }

    public BoundedUtf8Builder AppendLine(string? value)
    {
        Append(value);
        return AppendLine();
    }

    public override string ToString() => _builder.ToString();

    private void Reserve(int bytes)
    {
        if (bytes < 0 || _bytes > _maximumBytes - bytes)
            throw new PdfLimitException(_diagnostic);
        _bytes += bytes;
    }
}

/// <summary>Append-only stream that refuses growth beyond an export limit.</summary>
internal sealed class BoundedExportStream : Stream
{
    private readonly MemoryStream _inner = new();
    private readonly long _maximumBytes;
    private readonly string _diagnostic;

    public BoundedExportStream(long maximumBytes, string diagnostic)
    {
        if (maximumBytes < 1 || maximumBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        _maximumBytes = maximumBytes;
        _diagnostic = diagnostic;
    }

    public byte[] ToArray() => _inner.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        _inner.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _inner.WriteByte(value);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    private void EnsureCapacity(int count)
    {
        if (count < 0 || _inner.Position > _maximumBytes - count)
            throw new PdfLimitException(_diagnostic);
    }
}

/// <summary>Small cumulative counter for semantic or DOM export nodes.</summary>
internal sealed class ExportNodeBudget
{
    private readonly int _maximum;
    private readonly string _diagnostic;
    private int _used;

    public ExportNodeBudget(int maximum, string diagnostic)
    {
        if (maximum < 1)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        _maximum = maximum;
        _diagnostic = diagnostic;
    }

    public void Consume(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count > _maximum - _used)
            throw new PdfLimitException(_diagnostic);
        _used += count;
    }
}
