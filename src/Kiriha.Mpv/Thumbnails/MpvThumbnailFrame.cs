namespace Kiriha.Mpv;

/// <summary>
/// Represents an in-memory raw frame captured from mpv for timeline preview.
/// Contains uncompressed 32-bit BGRA pixel data without any filesystem I/O.
/// </summary>
public sealed record MpvThumbnailFrame(int Width, int Height, int Stride, byte[] BgraPixels);
