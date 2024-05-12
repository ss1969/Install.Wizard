using Helper;

namespace InstallerCreator;
using FILE_LENGTH_TYPE = long;

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
