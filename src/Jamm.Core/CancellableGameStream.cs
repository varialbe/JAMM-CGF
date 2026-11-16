namespace Jamm.Core;

/// <summary>Read-only archive stream that checks cancellation during hashing and archive parsing.</summary>
internal sealed class CancellableGameStream(string path, CancellationToken token)
    : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
{
    public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); return base.Read(buffer, offset, count); }
    public override int Read(Span<byte> buffer) { token.ThrowIfCancellationRequested(); return base.Read(buffer); }
    public override int ReadByte() { token.ThrowIfCancellationRequested(); return base.ReadByte(); }
    public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return base.Seek(offset, origin); }
}
