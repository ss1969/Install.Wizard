using System.Diagnostics;
using System.Text;
using Helper;

namespace InstallerCreator;

using TABLE_SIZE_TYPE = int;
using FILE_NAME_LENGTH_TYPE = int;
using FILE_LENGTH_TYPE = long;

/*
 Created output file structure:
  
    ORIGINAL_EXE_DATA
    FILE1_DATA
     ...
    FILE_N_DATA
    TABLE_ENTRY1
        FILE_NAME_LENGTH            4B
        FILE_NAME                   FILE_NAME_LENGTH
        FILE_LENGTH                 8B
        FILE_SHA                    64B
     ...
    TABLE_ENTRY_N
    TABLE_SIZE                      (4B)
    SHA_FROM_HEAD_TO_TABLE_ENTRY_N  (64B)
 */



class Program
{


    static void Main( string[] args )
    {
        string fileDirectory = args[0];
        string sourceEXE = args[1];
        string outputEXE = args[2];

        if ( args.Length < 3 )
        {
            LOG.Info( "Arguments not OK" );
            return;
        }
        LOG.Info( $"File Dir: \t\t{fileDirectory}\nInput: \t\t{sourceEXE}\nOutput: \t\t{outputEXE}" );

        // 1. 读取A下所有文件，计算SHA256
        var filesToAttach = Directory.GetFiles( fileDirectory );
        var fileInfoList = filesToAttach.Select( filePath => new FileDescriptor( filePath ) ).ToList();

        // 2. 生成描述表
        var fileTable = new FileInfoTable( fileInfoList );

        // 3. 按照描述表的顺序把文件链接到B文件结尾形成一个新的C文件
        using ( var streamSource = File.OpenRead( sourceEXE ) )
        using ( var streamOut = File.Create( outputEXE ) )
        {
            streamSource.CopyTo( streamOut );
            foreach ( var fileInfo in fileInfoList )
            {
                if ( !fileInfo.IsValid() ) return;
                using var fileStream = File.OpenRead( fileInfo.Name! );
                fileStream.CopyTo( streamOut );
            }
        }

        // 4. 最后再把描述表本身也链接上去
        using ( var streamOut = new FileStream( outputEXE, FileMode.Open, FileAccess.ReadWrite ) )
        {
            fileTable.WriteToStream( streamOut );
        }

        LOG.Info( "============= Start Read Back =============" );
        using ( var streamIn = new FileStream( outputEXE, FileMode.Open, FileAccess.Read ) )
        {
            fileTable.ReadFromStream( streamIn );
        }

    }
}

public class FileDescriptor
{
    public string? Name { get; set; }
    public FILE_LENGTH_TYPE Size { get; set; }
    public string? Sha256 { get; set; }

    public FileDescriptor( string name, long size, string sha256 )
    {
        Name = name;
        Size = size;
        Sha256 = sha256;
    }

    public FileDescriptor( in string filePath )
    {
        try
        {
            Name = filePath;
            Size = (uint)new FileInfo( filePath ).Length;
            using var stream = File.OpenRead( filePath );
            Sha256 = stream.CalculateSha256Hash();
        }
        catch ( Exception ) // FileNotFoundException, IOException
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

    private const int SHA256_STRLENGTH = 64;

    public FileInfoTable()
    {
        fileInfoList = [];
    }

    public FileInfoTable( List<FileDescriptor> fileInfoList )
    {
        this.fileInfoList = fileInfoList;
    }

    public readonly int FileCount => fileInfoList.Count;
    public readonly void Clear() => fileInfoList.Clear();

    /// <summary>
    /// Pad Table to end of stream
    /// </summary>
    /// <param name="stream"></param>
    public readonly void WriteToStream( Stream stream )
    {
        Int32 tableSize = 0;

        stream.Seek( 0, SeekOrigin.End );

        foreach ( var f in fileInfoList )
        {
            if ( !f.IsValid() ) continue;

            // write Name, NameLength, FileLength, Sha256 to stream
            tableSize += stream.Write<FILE_NAME_LENGTH_TYPE>( Encoding.UTF8.GetByteCount( f.Name! ) );
            tableSize += stream.WriteString( f.Name! );
            tableSize += stream.Write<FILE_LENGTH_TYPE>( f.Size );
            tableSize += stream.WriteString( f.Sha256!, Encoding.ASCII );

            LOG.Info( $"Table Entry:  {f.Name!}, NameLength {f.Name!.Length}, Size {f.Size}, SHA256 {f.Sha256!}" );
            LOG.Debug( $"     Table Size Now: {tableSize}" );
        }

        // calculate sha256 from start
        var currentPosition = stream.Position;
        stream.Seek( 0, SeekOrigin.Begin );
        var fullSHA = stream.CalculateSha256Hash();

        // write tableSize, sha256 to end
        stream.Seek( currentPosition, SeekOrigin.Begin );
        stream.Write<TABLE_SIZE_TYPE>( tableSize );
        stream.WriteString( fullSHA, Encoding.ASCII );

        LOG.Info( $"Whole Table Size {tableSize}, Whole SHA256 {fullSHA}" );
    }

    public bool ReadFromStream( Stream stream )
    {
        // compare total Sha32
        var shaCalc = stream.CalculateSha256Hash( stream.Length - SHA256_STRLENGTH - sizeof( TABLE_SIZE_TYPE ) );

        stream.Seek( -SHA256_STRLENGTH, SeekOrigin.End );
        var shaRead = stream.ReadString( SHA256_STRLENGTH, Encoding.ASCII ).Replace( "-", "" ).ToLower(); ;

        if ( !shaRead.Equals( shaCalc ) )
        {
            LOG.Info( $"Full File SHA Fail: ReadSHA {shaRead}, CalcSHA {shaCalc}" );
            return false;
        }
        LOG.Debug( "Full File SHA Passed!" );

        // read table Size
        stream.Seek( -SHA256_STRLENGTH - sizeof( TABLE_SIZE_TYPE ), SeekOrigin.End );
        TABLE_SIZE_TYPE tableSize = stream.Read<TABLE_SIZE_TYPE>();
        LOG.Debug( $"Table Size: {tableSize} Bytes" );

        // parse table
        stream.Seek( -SHA256_STRLENGTH - sizeof( TABLE_SIZE_TYPE ) - tableSize, SeekOrigin.End );
        fileInfoList.Clear();
        while ( tableSize > 0 )
        {
            var nameLength = stream.Read<FILE_NAME_LENGTH_TYPE>();
            var file = new FileDescriptor( stream.ReadString( nameLength ),
                                          stream.Read<FILE_LENGTH_TYPE>(),
                                          stream.ReadString( SHA256_STRLENGTH, Encoding.ASCII ) );
            fileInfoList.Add( file );
            tableSize -= sizeof( FILE_NAME_LENGTH_TYPE ) + nameLength + sizeof( FILE_LENGTH_TYPE ) + SHA256_STRLENGTH;
            LOG.Info( $"File Name: {file.Name}, Size {file.Size}, SHA256: {file.Sha256}" );
        }

        return true;
    }
}
