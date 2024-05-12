using System.Collections.Specialized;
using System.Diagnostics;
using Helper;
using static InstallerCreator.PackageOperater;

namespace InstallerCreator;
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
    static NameValueCollection ParseParameters(string[] args)
    {
        NameValueCollection parameters = [];

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('/') &&
                i + 1 < args.Length &&
                !args[i + 1].StartsWith('/'))
            {
                string parameter = args[i][1..].ToLower(); // 去掉前导斜杠,转小写
                parameters.Add(parameter, args[i + 1]);
            }
            i += 1;
        }

        return parameters;
    }

    static bool ValidateParameters(params string?[] parameters)
    {
        if (parameters == null) return false;
        foreach (var p in parameters)
        {
            Type programType = typeof(Program);
            var a = programType.GetField(p);
            var value = a.GetValue(programType);

            if (p == null)
            {
                LOG.Error($"Parameter '{nameof(p)}' is NULL");
                return false;
            }
            LOG.Info($"Parameter '{nameof(p)}' is {p}");
        }
        return true;
    }

    static void POCallback(EXTRACT_STATE s, Object? e)
    {
        LOG.Info($"{s} -> {e}");
    }

    static void Main(string[] args)
    {
        // Parameters
        NameValueCollection parameters = ParseParameters(args);

        var fileDirectory = parameters.Convert<string>("data");              // directory contains all files
        var source = parameters.Convert<string>("source");              // source EXE file
        var dest = parameters.Convert<string>("dest");              // output EXE file
        var mode = parameters.Convert<string>("mode");              // 'create' 'extract'
        var verbose = parameters.Convert<bool>("mode");              // show logs in console

        var po = new PackageOperater();
        po.ExtractStatus += POCallback;

        if (mode == "create")
        {
            //if (!ValidateParameters("fileDirectory", "source", "dest")) return;

            LOG.Info("Start Create");
            Debug.Assert(source != null);
            Debug.Assert(dest != null);
            Debug.Assert(fileDirectory != null);

            po.Create(source, dest, fileDirectory);
        }
        else if (mode == "extract")
        {
            //if (!ValidateParameters("source", "dest")) return;

            LOG.Info(message: "Start Extract");
            Debug.Assert(source != null);
            Debug.Assert(dest != null);

            po.Extract(source, dest);
        }
        else
        {
            LOG.Error("No valid 'mode' parameter set");
        }
    }

}
