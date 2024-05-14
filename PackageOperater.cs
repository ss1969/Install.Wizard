using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Helper;

namespace InstallerCreator;

public class PackageOperater
{
    public enum PO_STATE
    {
        PAK_START,
        PAK_FILE,
        PAK_SUCCESS,

        EXT_START,
        EXT_FILE,
        EXT_SUCCESS,

        PERCENTAGE,

        ERR_PATH_NOT_EXIST,
        ERR_DATA_SOURCE_EMPTY,
        ERR_PAK_COPY,
        ERR_PAK_SHA,
        ERR_PAK_COPY_TABLE,
        ERR_EXT_TABLE_READ,
        ERR_EXT_COPY,
        ERR_EXT_SHA,
    }

    public delegate void PoStateCallbackType( params object?[] args );
    public event PoStateCallbackType? PoStateCallback = null;

    public async Task Pack( string source, string dest, string fileDirectory )
    {
        // Input check
        if ( !Path.Exists( source ) )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_PATH_NOT_EXIST, source );
            return;
        }
        if ( !Path.Exists( Path.GetDirectoryName( Path.GetFullPath( dest ) ) ) )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_PATH_NOT_EXIST, Path.GetDirectoryName( Path.GetFullPath( dest ) )! );
            return;
        }
        if ( !Path.Exists( fileDirectory ) )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_PATH_NOT_EXIST, fileDirectory );
            return;
        }
        PoStateCallback?.Invoke( PO_STATE.PAK_START, fileDirectory, source, dest );

        // create file info list , then to fileTable
        var filesToAttach = Helper.Directory.GetAllFiles( fileDirectory );
        if ( filesToAttach == null )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_DATA_SOURCE_EMPTY, fileDirectory );
            return;
        }
        var fileInfoList = filesToAttach.Select( filePath => new FileDescriptor( filePath, fileDirectory ) ).ToList();

        // padding files
        using ( var streamSource = File.OpenRead( source ) )
        using ( var streamOut = File.Create( dest ) )
        {
            try
            {
                await streamSource.CopyToAsync( streamOut );
            }
            catch ( Exception ex )
            {
                PoStateCallback?.Invoke( PO_STATE.ERR_PAK_COPY, streamSource.Name, streamOut.Name, ex );
                return;
            }
            foreach ( var f in fileInfoList )
            {
                if ( !f.IsValid() ) continue;
                Debug.Assert( f.Name != null );

                PoStateCallback?.Invoke( PO_STATE.PAK_FILE, f.AbsolutePath );
                using var fileStream = File.OpenRead( f.AbsolutePath );
                try
                {
                    await fileStream.CopyToAsync( streamOut );
                }
                catch ( Exception ex )
                {
                    PoStateCallback?.Invoke( PO_STATE.ERR_PAK_COPY, f.Name, ex );
                    continue;
                }
            }
        }

        // padding the table
        try
        {
            var fileTable = new FileInfoTable( fileInfoList );
            using ( var streamOut = new FileStream( dest, FileMode.Open, FileAccess.ReadWrite ) )
            {
                fileTable.WriteToStream( streamOut );
            }
        }
        catch ( Exception ex )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_PAK_COPY_TABLE, ex );
            return;
        }

        // end
        PoStateCallback?.Invoke( PO_STATE.PAK_SUCCESS );
    }

    /// <summary>
    /// Extract files from source to dest directory.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="dest"></param>
    public async Task Extract( string dest, string? source = null, CancellationToken cts = new() )
    {
        // source check
        source ??= Environment.ProcessPath;
        if ( !Path.Exists( source ) )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_PATH_NOT_EXIST, source );
            return;
        }

        // create file table
        PoStateCallback?.Invoke( PO_STATE.EXT_START, source, dest );

        var fileTable = new FileInfoTable();
        using var streamIn = new FileStream( source, FileMode.Open, FileAccess.Read );

        var ret = fileTable.ReadFromStream( streamIn );
        if ( !ret )
        {
            PoStateCallback?.Invoke( PO_STATE.ERR_EXT_TABLE_READ, source );
            return;
        }
        Debug.Assert( fileTable.FileInfoList != null, "fileTable.FileInfoList is null" );

        // prepare dest directory
        if ( System.IO.Directory.Exists( dest ) )
        {
            System.IO.Directory.Delete( dest, true );
        }
        System.IO.Directory.CreateDirectory( dest );

        // start extract files, seek to an important position
        streamIn.Seek( fileTable.FileStartPosition(), SeekOrigin.End );
        foreach ( var f in fileTable.FileInfoList )
        {
            if ( cts.IsCancellationRequested ) return;
            if ( !f.IsValid() ) continue;
            Debug.Assert( f.Name != null );

            // start extract one file
            PoStateCallback?.Invoke( PO_STATE.EXT_FILE, f.Name );

            string? directoryPath = Path.GetDirectoryName( f.Name );
            if ( !String.IsNullOrEmpty( directoryPath ) && !System.IO.Directory.Exists( Path.Combine( dest, directoryPath ) ) )
            {
                System.IO.Directory.CreateDirectory( Path.Combine( dest, directoryPath )! );
            }

            // file data 
            using ( var streamOut = new FileStream( Path.Combine( dest, f.Name ), FileMode.Create, FileAccess.ReadWrite ) )
            {
                // zero size file specific
                if ( f.Size == 0 )
                {
                    PoStateCallback?.Invoke( PO_STATE.EXT_SUCCESS, f.Name, 0 );
                    continue;
                }

                // copy by size
                try
                {
                    await streamIn.CopyToNAsync( streamOut, (int)f.Size );
                }
                catch ( Exception ex )
                {
                    PoStateCallback?.Invoke( PO_STATE.ERR_EXT_COPY, f.Name, ex );
                    continue;
                }

                // compare SHA in table
                streamOut.Seek( 0, SeekOrigin.Begin );
                var calcSHA = streamOut.CalculateSha256Hash();
                PoStateCallback?.Invoke( calcSHA == f.Sha256 ? PO_STATE.EXT_SUCCESS : PO_STATE.ERR_EXT_SHA, f.Name, f.Size, calcSHA, f.Sha256! );
            }

            // percentage
            // TODO

        }
    }
}
