namespace Mk20Control.Protocol.Compat;

/// <summary>
/// Span-based <see cref="Stream"/> writes, which .NET Framework lacks.
/// </summary>
internal static class StreamCompat
{
    public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
        byte[] bytes = buffer.ToArray();
        stream.Write(bytes, 0, bytes.Length);
    }
}
