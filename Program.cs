using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.Unicode;

namespace InstallerCreator;

class Program
{
    static void Main(string[] args)
    {
        string fileDirectory = args[0];
        string sourceEXE = args[1];
        string outputEXE = args[2];

        // 1. 读取A下所有文件，计算SHA256
        var filesToAttach = Directory.GetFiles(fileDirectory);
        var fileInfoList = filesToAttach.Select(filePath => new FileDescriptor(filePath)).ToList();

        // 2. 生成描述表
        var fileTable = new FileInfoTable(fileInfoList);

        // 3. 按照描述表的顺序把文件链接到B文件结尾形成一个新的C文件
        using (var streamSource = File.OpenRead(sourceEXE))
        using (var streamOut = File.Create(outputEXE))
        {
            foreach (var fileInfo in fileInfoList)
            {
                if (!fileInfo.IsValid()) return;
                using var fileStream = File.OpenRead(fileInfo.Name);
                fileStream.CopyTo(streamOut);
            }

            streamSource.CopyTo(streamOut);
        }

        // 4. 最后再把描述表本身也链接上去
        using (var streamOut = File.OpenWrite(outputEXE))
        {
            fileTable.WriteToStream(streamOut);
        }
    }
}

public class FileDescriptor
{
    public string? Name { get; set; }
    public long Size { get; set; }
    public string? Sha256 { get; set; }

    public FileDescriptor(string name, long size, string sha256)
    {
        Name = name;
        Size = size;
        Sha256 = sha256;
    }

    public FileDescriptor(string filePath)
    {
        try
        {
            Name = filePath;
            Size = (uint)new FileInfo(filePath).Length;

            using var stream = File.OpenRead(filePath);
            Sha256 = Helper.CalculateSha256Hash(stream);
        }
        catch (Exception) // FileNotFoundException, IOException
        {
            Name = null;
            Size = 0;
            Sha256 = null;
        }
    }

    public bool IsValid()
    {
        return Name != null && Size > 0 && Sha256 != null;
    }

}

public struct FileInfoTable
{
    private List<FileDescriptor> fileInfoList;

    private const int SHA256_LENGTH = 32;

    public FileInfoTable()
    {
        fileInfoList = [];
    }

    public FileInfoTable(List<FileDescriptor> fileInfoList)
    {
        this.fileInfoList = fileInfoList;
    }

    public readonly int FileCount => fileInfoList.Count;
    public readonly void Clear() => fileInfoList.Clear();

    public readonly void WriteToStream(Stream stream)
    {
        Int32 tableSize = 0;

        stream.Seek(0, SeekOrigin.End);

        foreach (var f in fileInfoList)
        {
            if (!f.IsValid()) continue;

            // write Name, NameLength, FileLength, Sha256 to stream
            tableSize += stream.Write<int>(f.Name!.Length);
            tableSize += stream.WriteString(f.Name!);
            tableSize += stream.Write<long>(f.Size);
            tableSize += stream.WriteString(f.Sha256!, Encoding.ASCII);

            Trace.WriteLine($"+ File {f.Name!}, NameLength {f.Name!.Length}, Size {f.Size}, SHA256 {f.Sha256!} => Table {tableSize}");
        }

        // write tableSize, tableSha256
        var fS = Helper.CalculateSha256Hash(stream);
        stream.Write<int>(tableSize);
        stream.WriteString(fS);

        Trace.WriteLine($"Whole Table Size {tableSize}, Whole SHA256 {fS}");
    }

    public bool ReadFromStream(Stream stream)
    {
        // compare total Sha32
        var shaCalc = Helper.CalculateSha256Hash(stream, stream.Length - SHA256_LENGTH);

        stream.Seek(-SHA256_LENGTH, SeekOrigin.End);
        var shaRead = stream.ReadString(SHA256_LENGTH);

        if (!shaRead.Equals(shaCalc))
        {
            Trace.WriteLine($"Fail: ReadSHA {shaRead}, CalcSHA {shaCalc}");
            return false;
        }

        // read table Size
        stream.Seek(-SHA256_LENGTH - sizeof(int), SeekOrigin.End);
        int tableSize = stream.Read<int>();

        // read table
        var table = new byte[tableSize];
        stream.Seek(-SHA256_LENGTH - sizeof(int) - tableSize, SeekOrigin.End);
        stream.Read(table, 0, tableSize);

        // parse table
        fileInfoList.Clear();
        while (tableSize > 0)
        {
            var nameLength = stream.Read<int>();
            fileInfoList.Add(new FileDescriptor(stream.ReadString(nameLength), stream.Read<int>(), stream.ReadString(SHA256_LENGTH)));
            tableSize -= nameLength + sizeof(int) + sizeof(int) + SHA256_LENGTH;
        }

        return true;
    }
}
