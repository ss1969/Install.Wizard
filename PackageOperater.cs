using System.Diagnostics;
using Helper;

namespace InstallerCreator;

public class PackageOperater
{
    public enum EXTRACT_STATE
    {
        START,
        SUCCESS,
        FAIL,
        PERCENTAGE
    }
    public event Action<EXTRACT_STATE, Object?>? ExtractStatus = null;

    public void Create(string source, string dest, string fileDirectory)
    {
        Debug.Assert( Path.Exists( source ) );
        Debug.Assert( Path.Exists( dest ) );
        Debug.Assert( Path.Exists( fileDirectory ) );

        LOG.Info($"File Dir: \t{fileDirectory}\nInput: \t\t{source}\nOutput: \t{dest}");

        // create file info list , then to fileTable
        var filesToAttach = Directory.GetFiles(fileDirectory, );
        var fileInfoList = filesToAttach.Select(filePath => new FileDescriptor(filePath)).ToList();

        // padding files
        using (var streamSource = File.OpenRead(source))
        using (var streamOut = File.Create(dest))
        {
            streamSource.CopyTo(streamOut);
            foreach (var f in fileInfoList)
            {
                if (!f.IsValid()) continue;
                Debug.Assert(f.Name != null);
                Debug.Assert(f.Size != 0);
                Debug.Assert(f.Sha256 != null);

                using var fileStream = File.OpenRead(f.Name);
                fileStream.CopyTo(streamOut);
            }
        }

        // padding the table
        var fileTable = new FileInfoTable(fileInfoList);
        using (var streamOut = new FileStream(dest, FileMode.Open, FileAccess.ReadWrite))
        {
            fileTable.WriteToStream(streamOut);
        }

        LOG.Info("End");
    }

    /// <summary>
    /// Extract files from source to dest directory.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="dest"></param>
    public void Extract(string source, string dest, CancellationToken cts = new())
    {
        Debug.Assert(Path.Exists(source));

        // create file table
        LOG.Info("Create File Info Table");
        var fileTable = new FileInfoTable();
        using (var streamIn = new FileStream(source, FileMode.Open, FileAccess.Read))
        {
            var ret = fileTable.ReadFromStream(streamIn);
            Debug.Assert(ret, "fileTable.ReadFromStream failed");
        }
        Debug.Assert(fileTable.FileInfoList != null, "fileTable.FileInfoList is null");

        // prepare dest directory
        LOG.Info($"Prepare directory '{dest}'");
        if (Directory.Exists(dest))
        {
            Directory.Delete(dest, true);
        }
        Directory.CreateDirectory(dest);

        // start extract files task
        LOG.Info($"Extracting Files");
        Task.Factory.StartNew(async () =>
        {
            using var streamIn = new FileStream(source, FileMode.Open, FileAccess.Read);
            streamIn.Seek(fileTable.FileStartPosition(), SeekOrigin.Begin);

            foreach (var f in fileTable.FileInfoList)
            {
                if (!f.IsValid()) continue;
                Debug.Assert(f.Name != null);
                Debug.Assert(f.Size != 0);
                Debug.Assert(f.Sha256 != null);

                if (cts.IsCancellationRequested) return;

                // start extract one file
                ExtractStatus?.Invoke(EXTRACT_STATE.START, f.Name);

                string? directoryPath = Path.GetDirectoryName(f.Name);
                if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath!);

                using var streamOut = new FileStream(Path.Combine(dest, f.Name), FileMode.Open, FileAccess.ReadWrite);
                await streamOut.CopyToAsync(streamOut, (int)f.Size);

                // compare SHA in table
                var calcSHA = streamOut.CalculateSha256Hash();
                if (calcSHA == f.Sha256) ExtractStatus?.Invoke(EXTRACT_STATE.SUCCESS, f.Name);
                {
                    ExtractStatus?.Invoke(EXTRACT_STATE.FAIL, f.Name);
                    LOG.Info($"Extracted file {f.Name} SHA256 {calcSHA} neq Writen {f.Sha256}");
                }

                // percentage

            }

        }).ContinueWith(t =>
        {
            LOG.Info("End");
        }, TaskContinuationOptions.OnlyOnRanToCompletion);

    }
}
