﻿using System.Collections.Specialized;
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

    static bool ValidateParameters( NameValueCollection pc, params string?[] parameters)
    {
        if (parameters == null) return false;
        foreach (var p in parameters)
        {
            if ( pc.GetValues(p) == null)
            {
                LOG.Error($"Parameter '{p}' is NULL");
                return false;
            }
            LOG.Info($"Parameter '{p}' is {pc.GetValues( p )?.FirstOrDefault()}");
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

        var data = parameters.Convert<string>("data");              // directory contains all files
        var source = parameters.Convert<string>("source");              // source EXE file
        var dest = parameters.Convert<string>("dest");              // output EXE file
        var mode = parameters.Convert<string>("mode");              // 'create' 'extract'
        var verbose = parameters.Convert<bool>("verbose");              // show logs in console

        var po = new PackageOperater();
        po.ExtractStatus += POCallback;

        if (mode == "create")
        {
            LOG.Info("Start Create");

            if (!ValidateParameters( parameters, "data", "source", "dest")) return;
            Debug.Assert(source != null);
            Debug.Assert(dest != null);
            Debug.Assert(data != null);

            if ( !Path.Exists( source ) )
            {
                LOG.Error( "Source file NOT exists" );
                return;
            }
            if ( !Path.Exists( dest ) )
            {
                LOG.Error( "Dest file NOT exists" );
                return;
            }
            if ( !Path.Exists( data ) )
            {
                LOG.Error( "Input file directory NOT exists" );
                return;
            }

            po.Create(source, dest, data);
        }
        else if (mode == "extract")
        {
            LOG.Info(message: "Start Extract");

            if (!ValidateParameters( parameters, "source", "dest")) return;
            Debug.Assert(source != null);
            Debug.Assert(dest != null);

            if ( !Path.Exists( source ) )
            {
                LOG.Error( "Source file NOT exists" );
                return;
            }

            po.Extract(source, dest);
        }
        else
        {
            LOG.Error("No valid 'mode' parameter set");
        }
    }

}
