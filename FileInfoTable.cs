using System.Diagnostics;
using System.Text;
using Helper;

namespace InstallerCreator;

//TODO: error / info report change to callback


using TABLE_SIZE_TYPE = int;
using FILE_NAME_LENGTH_TYPE = int;
using FILE_LENGTH_TYPE = long;

public class FileInfoTable
{
    private const int SHA256_STRLENGTH = 64;    //  32 bytes SHA256 => 64 byte encoding to ASCII
    public List<FileDescriptor>? FileInfoList { get; private set; }
    public int FileCount => FileInfoList?.Count ?? 0;
    public void Clear() => FileInfoList?.Clear();
    public int TableSize { get; private set; }
    public long FileSizeTotal { get; private set; }

    public long FileStartPosition() => -(FileSizeTotal + TableSize + sizeof(TABLE_SIZE_TYPE) + SHA256_STRLENGTH);

    public FileInfoTable(List<FileDescriptor>? fileInfoList = null)
    {
        FileInfoList = fileInfoList ?? [];
        TableSize = 0;
        FileSizeTotal = 0;
    }

    /// <summary>
    /// Pad Table to end of stream
    /// </summary>
    /// <param name="stream"></param>
    public void WriteToStream(Stream stream)
    {
        if (FileInfoList == null) return;

        TableSize = 0;
        FileSizeTotal = 0;

        stream.Seek(0, SeekOrigin.End);

        foreach (var f in FileInfoList)
        {
            if (!f.IsValid()) continue;
            Debug.Assert( f.Name != null );
            Debug.Assert( f.Sha256 != null );

            // write Name, NameLength, FileLength, Sha256 to stream
            TableSize += stream.Write<FILE_NAME_LENGTH_TYPE>(Encoding.UTF8.GetByteCount(f.Name));
            TableSize += stream.WriteString(f.Name);
            TableSize += stream.Write<FILE_LENGTH_TYPE>(f.Size);
            TableSize += stream.WriteString(f.Sha256, Encoding.ASCII);
            FileSizeTotal += f.Size;

            LOG.Info($"Table Entry:  {f.Name}, NameLength {f.Name.Length}, Size {f.Size}, SHA256 {f.Sha256}");
            LOG.Debug($"     Table Size Now: {TableSize}");
        }

        // calculate sha256 from start
        var currentPosition = stream.Position;
        stream.Seek(0, SeekOrigin.Begin);
        var fullSHA = stream.CalculateSha256Hash();

        // write tableSize, sha256 to end
        stream.Seek(currentPosition, SeekOrigin.Begin);
        stream.Write<TABLE_SIZE_TYPE>(TableSize);
        stream.WriteString(fullSHA, Encoding.ASCII);

        LOG.Info($"Whole Table Size {TableSize}, Whole SHA256 {fullSHA}");
    }

    public bool ReadFromStream(Stream stream)
    {
        if (FileInfoList == null) return false;

        // compare total Sha32
        var shaCalc = stream.CalculateSha256Hash(stream.Length - SHA256_STRLENGTH - sizeof(TABLE_SIZE_TYPE));

        stream.Seek(-SHA256_STRLENGTH, SeekOrigin.End);
        var shaRead = stream.ReadString(SHA256_STRLENGTH, Encoding.ASCII).Replace("-", "").ToLower();

        if (!shaRead.Equals(shaCalc))
        {
            LOG.Info($"Full File SHA Fail: ReadSHA {shaRead}, CalcSHA {shaCalc}");
            return false;
        }
        LOG.Debug("Full File SHA Passed!");

        // read table Size
        stream.Seek(-SHA256_STRLENGTH - sizeof(TABLE_SIZE_TYPE), SeekOrigin.End);
        TableSize = stream.Read<TABLE_SIZE_TYPE>();
        LOG.Debug($"Table Size: {TableSize} Bytes");

        // seek to start of table
        stream.Seek(-SHA256_STRLENGTH - sizeof(TABLE_SIZE_TYPE) - TableSize, SeekOrigin.End);

        // parse table
        FileInfoList.Clear();
        int t = TableSize;
        while (t > 0)
        {
            var nameLength = stream.Read<FILE_NAME_LENGTH_TYPE>();
            var name = stream.ReadString(nameLength);
            var size = stream.Read<FILE_LENGTH_TYPE>();
            var sha256 = stream.ReadString(SHA256_STRLENGTH, Encoding.ASCII);

            FileInfoList.Add(new FileDescriptor(name, size, sha256));
            FileSizeTotal += size;
            LOG.Info($"File Name: {name}, Size {size}, SHA256: {sha256}");
         
            t -= sizeof(FILE_NAME_LENGTH_TYPE) + nameLength + sizeof(FILE_LENGTH_TYPE) + SHA256_STRLENGTH;
        }

        return true;
    }
}
