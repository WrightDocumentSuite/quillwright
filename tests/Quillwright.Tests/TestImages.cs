namespace Quillwright.Tests;

/// <summary>Small encoded images so tests never touch the file system.</summary>
internal static class TestImages
{
    /// <summary>A 2×2 opaque PNG.</summary>
    public static byte[] Png { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR4nGP8z8DwnwEJMKEL0FMQAG" +
        "0lAgcqA5wCAAAAAElFTkSuQmCC");
}
