// Helpers for APIs this tool used that .NET Framework does not provide.

internal static class Compat
{
    /// <summary>Quotes and joins process arguments; ProcessStartInfo.ArgumentList is .NET Core only.</summary>
    public static string BuildArguments(params string[] arguments) =>
        string.Join(" ", arguments.Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));

    /// <summary>Uppercase hex, matching Convert.ToHexString (.NET 5+).</summary>
    public static string ToHex(byte[] bytes) =>
        BitConverter.ToString(bytes).Replace("-", "");

    public static string ToHex(byte[] bytes, int start, int length) =>
        BitConverter.ToString(bytes, start, length).Replace("-", "");

    /// <summary>Parses uppercase hex, matching Convert.FromHexString (.NET 5+).</summary>
    public static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
