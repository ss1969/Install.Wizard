using System.Diagnostics;
using Helper;

namespace InstallerCreator;

public class PackageOperater
{
    public enum EXTRACT_STATE
    {
        START,
        SUCCESS,
        FAIL_COPY,
        FAIL_SHA256,
        PERCENTAGE
    }
    public event Action<EXTRACT_STATE, Object?>? ExtractStatus = null;

    public void Create(string source, string dest, string fileDirectory)
    {
        Debug.Assert(Path.Exists(source));
        Debug.Assert(Path.Exists(dest));
        Debug.Assert(Path.Exists(fileDirectory));

        LOG.Info($"File Dir: \t{fileDirectory}\nInput: \t\t{source}\nOutput: \t{dest}");

        // create file info list , then to fileTable
        var filesToAttach = Helper.Directory.GetAllFiles(fileDirectory);
        if (filesToAttach == null)
        {
            LOG.Info($"No files exist in {fileDirectory}");
            return;
        }
        var fileInfoList = filesToAttach.Select(filePath => new FileDescriptor(filePath, fileDirectory)).ToList();

        // padding files
        using (var streamSource = File.OpenRead(source))
        using (var streamOut = File.Create(dest))
        {
            streamSource.CopyTo(streamOut);
            foreach (var f in fileInfoList)
            {
                if (!f.IsValid()) continue;
                Debug.Assert(f.Name != null);

                using var fileStream = File.OpenRead(f.AbsolutePath);
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
        using var streamIn = new FileStream(source, FileMode.Open, FileAccess.Read);

        var ret = fileTable.ReadFromStream(streamIn);
        Debug.Assert(ret, "fileTable.ReadFromStream failed");
        Debug.Assert(fileTable.FileInfoList != null, "fileTable.FileInfoList is null");

        // prepare dest directory
        LOG.Info($"Prepare directory '{dest}'");
        if (System.IO.Directory.Exists(dest))
        {
            System.IO.Directory.Delete(dest, true);
        }
        System.IO.Directory.CreateDirectory(dest);

        // start extract files, seek to an important position
        LOG.Info($"Extracting Files");
        streamIn.Seek(fileTable.FileStartPosition(), SeekOrigin.End);

        foreach (var f in fileTable.FileInfoList)
        {
            if (cts.IsCancellationRequested) return;
            if (!f.IsValid()) continue;
            Debug.Assert(f.Name != null);

            // start extract one file
            ExtractStatus?.Invoke(EXTRACT_STATE.START, f.Name);

            // check sub-directory in output folder
            string? directoryPath = Path.GetDirectoryName(f.Name);
            if (!String.IsNullOrEmpty(directoryPath) && !System.IO.Directory.Exists(Path.Combine(dest, directoryPath)))
            {
                System.IO.Directory.CreateDirectory(Path.Combine(dest, directoryPath)!);
            }

            // file data 
            using (var streamOut = new FileStream(Path.Combine(dest, f.Name), FileMode.Create, FileAccess.ReadWrite))
            {
                // zero size file specific
                if (f.Size == 0) continue;

                // copy by size
                try
                {
                    streamIn.CopyToN(streamOut, (int)f.Size);
                }
                catch (Exception ex)
                {
                    ExtractStatus?.Invoke(EXTRACT_STATE.FAIL_COPY, f.Name + " " + ex);
                    continue;
                }

                // compare SHA in table
                streamOut.Seek(0, SeekOrigin.Begin);
                var calcSHA = streamOut.CalculateSha256Hash();
                if (calcSHA == f.Sha256)
                    ExtractStatus?.Invoke(EXTRACT_STATE.SUCCESS, f.Name);
                else
                {
                    ExtractStatus?.Invoke(EXTRACT_STATE.FAIL_SHA256, f.Name);
                    LOG.Info($"Extracted file {f.Name} SHA256 {calcSHA} neq Writen {f.Sha256}");
                }
            }

            // percentage
            // TODO

        }
    }
}
