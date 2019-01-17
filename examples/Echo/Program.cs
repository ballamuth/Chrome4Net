﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Resources;
using Microsoft.Win32;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Echo
{
    class Program
    {
        private static ILog log = LogManager.GetLogger(typeof(Program));
        private static Options options = new Options();
        private static Properties.Settings settings = Properties.Settings.Default;
        
        static int Main(string[] args)
        {
            // configure log4net
            log4net.Config.XmlConfigurator.Configure();

            log.Info("application started");
            log.DebugFormat("command line : \"{0}\"", string.Join("\", \"", args));

            // started with no arguments?
            if (args.Length == 0) Usage();

            // started by chrome?
            else if (args.Any(arg => arg.StartsWith("chrome-extension://"))) RunNativeMessagingHost(args);

            // register command?
            else if (args.Contains("register")) RegisterNativeMessagingHost(args);

            // invalid command line
            else InvalidCommand(args[args.Length - 1]);

            log.Info("application stopped");
            return 0;
        }

        static int RunNativeMessagingHost(string[] args)
        {
            Host host = new Host();
            host.Run();
            return 0;
        }

        static int RegisterNativeMessagingHost(string[] args)
        {
            foreach(string arg in args) {
                if (arg == "register") continue;
                else if (arg.StartsWith("--hive=")) options.hive = arg.Remove(0, "--hive=".Length);
                else if (arg.StartsWith("--manifest=")) options.manifest = arg.Remove(0, "--manifest=".Length);
                else return InvalidOption(arg);
            }

            // registry key
            string keyName;
            if (options.hive == "HKCU")
            {
                keyName = "HKEY_CURRENT_USER\\Software\\Google\\Chrome\\NativeMessagingHosts\\chrome4net.echo";
            }
            else if (options.hive == "HKLM")
            {
                keyName = "HKEY_LOCAL_MACHINE\\Software\\Google\\Chrome\\NativeMessagingHosts\\chrome4net.echo";
            }
            else return InvalidOptionValue("--hive", options.hive);

            try
            {
                Console.WriteLine("Creating this host manifest:");
                Console.WriteLine("{0}", options.manifest);
                StreamWriter manifest = File.CreateText(options.manifest);
                manifest.Write( new JObject(
                        new JProperty("name", "chrome4net.echo"),
                        new JProperty("description", "Chrome4Net Example Echo Extension"),
                        new JProperty("type", "stdio"),
                        new JProperty("path", System.Reflection.Assembly.GetEntryAssembly().Location),
                        new JProperty("allowed_origins", 
                            new JArray(
                                new JValue( string.Format("chrome-extension://{0}/", settings.ExtensionId) )
                                )
                            )
                    ).ToString()
                    );
                manifest.Close();
                Console.WriteLine("Manifest created successfully");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error error creating the host manifest:", ex.Message);
                Console.Error.WriteLine(ex);
                return 0;
            }

            try 
            {
                Console.WriteLine("Registering this host:");
                Console.WriteLine("[{0}]", keyName);
                Console.WriteLine("@=\"{0}\"", options.manifest.Replace("\\","\\\\"));
                Microsoft.Win32.Registry.SetValue(keyName, null, options.manifest);
                Console.WriteLine("Host registered successfully");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error registering the host:",ex.Message);
                return 0;
            }

            return 0;
        }

        static int InvalidCommand(string command)
        {
            TextWriter tw = Console.Error;
            tw.WriteLine("Invalid command line : unknown command '{0}'. Start again with no parameters to get usage information.", command);
            return 0;
        }

        static int InvalidOption(string option)
        {
            TextWriter tw = Console.Error;
            tw.WriteLine("Invalid command line : unknown option '{0}'. Start again with no parameters to get usage information.", option);
            return 0;
        }

        static int InvalidOptionValue(string option, string value)
        {
            TextWriter tw = Console.Error;
            tw.WriteLine("Invalid command line : invalid option '{0}' value '{1}'. Start again with no parameters to get usage information.", option, value);
            return 0;
        }

        static int Usage(TextWriter tw = null)
        {
            if (tw == null) tw = Console.Out;
            tw.WriteLine("Chrome4Net Echo Example Extension. Author Konstantin Kuzvesov, 2015");
            tw.WriteLine("Usage: {0} [options] <command>", Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location));
            tw.WriteLine();
            tw.WriteLine("Commands with options");
            tw.WriteLine();
            tw.WriteLine("  register                 Register this host");
            tw.WriteLine("    --hive=<HKCU|HKLM>     The hive to register the host in (default is {0})", options.hive);
            tw.WriteLine("    --manifest=<file>      The file to output this host manifest to (default is {0}; overwritten, if exists)", options.manifest);
            tw.WriteLine();
            tw.WriteLine("  chrome-extension://*/    Start a native messaging host");
            tw.WriteLine("    --parent-window=*      Specify parent window id");
            tw.WriteLine();
            return 0;
        }
   
    }

    sealed class Options
    {
        public string hive = "HKCU";
        public string manifest =
            Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\" +
            Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location) + ".manifest.json";
    }
}
