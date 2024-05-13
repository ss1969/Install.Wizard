using System.Diagnostics;
using Helper;

namespace InstallerCreator;
using FILE_LENGTH_TYPE = long;

public class FileDescriptor
{
    public string? Name { get; set; }
    public string AbsolutePath {  get; set; } = string.Empty;
    public FILE_LENGTH_TYPE Size { get; set; }
    public string? Sha256 { get; set; }

    // for extract use
    public FileDescriptor(string name, long size, string sha256)
    {
        Name = name;
        Size = size;
        Sha256 = sha256;
    }

    // for create table use
    public FileDescriptor(string filePath, string fileDirectory = "")
    {
        Debug.Assert(filePath != null);

        try
        {
            Name = Path.GetRelativePath(fileDirectory, filePath);   // set file path to relative value
            AbsolutePath = filePath;
            using var stream = File.OpenRead(filePath);
            Size = stream.Length;
            Sha256 = stream.CalculateSha256Hash();
            
            //if (filePath.StartsWith(fileDirectory)) { filePath = filePath[fileDirectory.Length..]; }
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
        return Name != null;
    }

}
