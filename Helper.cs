using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Helper;

public static class LOG
{
    public static void Info(string message)
    {
        Console.WriteLine(message);
        Trace.WriteLine(message);
    }
    public static void Debug(string message)
    {
        Trace.WriteLine(message);
    }
    public static void Error(string message)
    {
        Console.WriteLine(message);
        Trace.WriteLine(message);
    }
}

public static class NameValueCollectionExtensions
{
    public static T? Convert<T>(this NameValueCollection c, string v)
    {
        if (typeof(T) == typeof(string))
        {
            return (T?)(object?)c.GetValues(v)?.FirstOrDefault();
        }
        else if (typeof(T) == typeof(bool))
        {
            if (c.GetValues(v) == null) return (T)(Object)false;
            return (T?)(object?)c.GetValues(v)?.FirstOrDefault().ToBool();
        }
        return default;
    }
}

public static class StringExtensions
{
    public static bool ToBool(this string? str) => str?.ToLower() == "true";
}

public static class StreamExtensions
{
    public static string CalculateSha256Hash(this Stream stream, long size = -1)
    {
        const int BLOCK_SIZE = 4096;
        byte[] buffer = new byte[BLOCK_SIZE];

        using SHA256 sha256 = SHA256.Create();
        int bytesRead;
        long totalBytesRead = 0;

        if (size == -1)
        {
            while ((bytesRead = stream.Read(buffer, 0, BLOCK_SIZE)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalBytesRead += bytesRead;
            }
        }
        else
        {
            while (totalBytesRead < size &&
                    (bytesRead = stream.Read(buffer, 0, (int)Math.Min(size - totalBytesRead, BLOCK_SIZE))) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalBytesRead += bytesRead;
            }
        }

        sha256.TransformFinalBlock(buffer, 0, 0);

        Debug.Assert(sha256.Hash != null);
        LOG.Debug($"Calculate SHA : data {totalBytesRead} bytes, size Param: {size}");
        return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLower();
    }

    public static T Read<T>(this Stream stream) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];

        stream.Read(buffer, 0, size);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(buffer, 0, ptr, size);
#pragma warning disable IL2091 // Target generic argument does not satisfy 'DynamicallyAccessedMembersAttribute' in target method or type. The generic parameter of the source method or type does not have matching annotations.
        T result = Marshal.PtrToStructure<T>(ptr);
#pragma warning restore IL2091 // Target generic argument does not satisfy 'DynamicallyAccessedMembersAttribute' in target method or type. The generic parameter of the source method or type does not have matching annotations.
        Marshal.FreeHGlobal(ptr);

        return result;
    }

    public static int Write<T>(this Stream stream, T value) where T : struct
    {
        int size = Marshal.SizeOf(value);
        byte[] buffer = new byte[size];

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.StructureToPtr(value, ptr, true);
        Marshal.Copy(ptr, buffer, 0, size);
        Marshal.FreeHGlobal(ptr);

        stream.Write(buffer, 0, buffer.Length);

        return buffer.Length;
    }

    public static int WriteInt(this Stream stream, int value)
    {
        stream.Write(BitConverter.GetBytes(value), 0, sizeof(int));
        return sizeof(int);
    }

    /// <summary>
    /// Read from stream to get a string.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="byteSize"> Must be a correct value or the string returned is useless </param>
    /// <returns></returns>
    public static string ReadString(this Stream stream, int byteSize, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var buffer = new byte[byteSize];
        stream.Read(buffer, 0, byteSize);
        return encoding.GetString(buffer);
    }

    /// <summary>
    /// Write string to stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="value"></param>
    /// <param name="encoding"> what encoding to write, default UTF-8 </param>
    /// <returns> bytes written count </returns>
    public static int WriteString(this Stream stream, string value, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        var buffer = encoding.GetBytes(value);
        stream.Write(buffer, 0, buffer.Length);
        return buffer.Length;
    }

    /// <summary>
    /// Copy from source to dest with specific size of data.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="dest"></param>
    /// <param name="size"></param>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static void CopyToN(this Stream source, Stream dest, long size)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);
        if (!source.CanRead)
            throw new InvalidOperationException("Source stream cannot be read.");
        if (!dest.CanWrite)
            throw new InvalidOperationException("Destination stream cannot be written to.");
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size), "The size must be non-negative.");

        // create buffer
        byte[] buffer = new byte[8192];
        long totalBytesRead = 0;
        int bytesRead;

        while (totalBytesRead < size && (bytesRead = source.Read(buffer, 0, (int)Math.Min(buffer.Length, size - totalBytesRead))) > 0)
        {
            dest.Write(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;
        }
    }
}

public static class Directory
{
    public static List<string>? GetAllFiles(string directoryPath)
    {
        List<string> files = [];

        string[] filePaths = System.IO.Directory.GetFiles(directoryPath);
        string[] subDirectoryPaths = System.IO.Directory.GetDirectories(directoryPath);

        files.AddRange(filePaths);

        foreach (string subDirectoryPath in subDirectoryPaths)
        {
            var n = GetAllFiles(subDirectoryPath);
            if (n != null) files.AddRange(n);
        }

        return files;
    }

}
