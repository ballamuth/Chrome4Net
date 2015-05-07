using System;
using System.IO;
using log4net;
using Newtonsoft.Json.Linq;

namespace TwainScan
{
    class Program
    {
        private static ILog log = LogManager.GetLogger(typeof(Program));

        public enum ExitCode
        {
            Success = 0,
            Error = -1,
            InvalidCommandLine = -2,
        };

        public const string ExtensionId = "amjfkhhbfhdhjlgfhjkhhjncfapcacmd";
        public const string ExtensionUrl = "chrome-extension://amjfkhhbfhdhjlgfhjkhhjncfapcacmd/";
        public const int SyncTimeout = 30 * 1000;

        /// <summary>
        /// Program options.
        /// </summary>        
        public sealed class Options
        {
            public string hive = "HKCU";
            public string manifest =
                Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\" +
                Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location) + ".manifest.json";
            public string parentWindow;
            public string pipeIn;
            public string pipeOut;
        }
        private static Options options = new Options();


        /// <summary>
        /// Program entry point.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            // configure log4net
            log4net.GlobalContext.Properties["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            log4net.Config.XmlConfigurator.Configure();

            // log start
            log.DebugFormat("application started with parameters=\"{0}\"", string.Join("\", \"", args));

            // do the job
            int exitCode = (int)ExitCode.Success;
            try
            {
                if (args.Length == 0) exitCode = Usage();
                else if (args[args.Length - 1].ToLower().StartsWith("chrome-extension://")) exitCode = Relay(args);
                else if (args[args.Length - 1].ToLower() == "process") exitCode = Process(args);
                else if (args[args.Length - 1].ToLower() == "register") exitCode = Register(args);
                else exitCode = InvalidCommand(args[args.Length - 1]);
            }
            catch (Exception ex)
            {
                log.Fatal("Unhandled exception.", ex);
                exitCode = (int)ExitCode.Error;
            }

            // log stop
            log.Debug("application stopped");

            // return exitcode
            return exitCode;
        }

        /// <summary>
        /// Starts native messages relay.
        /// </summary>
        static int Relay(string[] args)
        {
            log.Debug("parse command line options");
            foreach (string arg in args)
            {
                if (arg.ToLower().StartsWith("chrome-extension://")) continue;
                else if (arg.ToLower().StartsWith("--parent-window=")) options.parentWindow = arg.Remove(0, "--parent-window=".Length);
                else return InvalidOption(arg);
            }
            log.DebugFormat("options --parent-window={0}", options.parentWindow);

            new Relay(options).Sync(SyncTimeout).Run();

            return (int)ExitCode.Success;
        }

        /// <summary>
        /// Starts native messages processor.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Process(string[] args)
        {
            log.Debug("parse command line options");
            foreach (string arg in args)
            {
                if (arg.ToLower() == "process") continue;
                else if (arg.ToLower().StartsWith("--pipe-in=")) options.pipeIn = arg.Remove(0, "--pipe-in=".Length);
                else if (arg.ToLower().StartsWith("--pipe-out=")) options.pipeOut = arg.Remove(0, "--pipe-out=".Length);
                else return InvalidOption(arg);
            }
            if (options.pipeIn == null) return RequiredOptionHasNoValue("--pipe-in");
            if (options.pipeOut == null) return RequiredOptionHasNoValue("--pipe-out");
            log.DebugFormat("options --pipe-in={0}", options.pipeIn);
            log.DebugFormat("options --pipe-out={0}", options.pipeOut);

            new Scan(options).Sync(SyncTimeout).Run();

            return (int)ExitCode.Success;
        }

        /// <summary>
        /// Registers this host.
        /// </summary>
        static int Register(string[] args)
        {
            log.Debug("parse command line options");
            foreach (string arg in args)
            {
                if (arg.ToLower() == "register") continue;
                else if (arg.ToLower().StartsWith("--hive=")) options.hive = arg.Remove(0, "--hive=".Length);
                else if (arg.ToLower().StartsWith("--manifest=")) options.manifest = arg.Remove(0, "--manifest=".Length);
                else return InvalidOption(arg);
            }
            string keyName;
            if (options.hive.ToUpper() == "HKCU")
            {
                keyName = "HKEY_CURRENT_USER\\Software\\Google\\Chrome\\NativeMessagingHosts\\chrome4net.twainscan";
            }
            else if (options.hive.ToUpper() == "HKLM")
            {
                keyName = "HKEY_LOCAL_MACHINE\\Software\\Google\\Chrome\\NativeMessagingHosts\\chrome4net.twainscan";
            }
            else return InvalidOptionValue("--hive", options.hive);

            log.DebugFormat("options --hive={0}", options.hive);
            log.DebugFormat("options --manifest={0}", options.manifest);

            log.Debug("create native messaging host manifest");
            TextWriter tw = Console.Error;
            try
            {
                tw.WriteLine("Creating this host manifest:");
                tw.WriteLine("{0}", options.manifest);
                StreamWriter manifest = File.CreateText(options.manifest);
                manifest.Write(new JObject(
                        new JProperty("name", "chrome4net.twainscan"),
                        new JProperty("description", "Chrome4Net Twain Scan Extension"),
                        new JProperty("type", "stdio"),
                        new JProperty("path", System.Reflection.Assembly.GetEntryAssembly().Location),
                        new JProperty("allowed_origins",new JArray(new JValue(string.Format("chrome-extension://{0}/", ExtensionId))))
                    ).ToString());
                manifest.Close();
                log.Debug("manifest created successfully");
                tw.WriteLine("Manifest created successfully");
                tw.WriteLine();
            }
            catch (Exception ex)
            {
                log.Error("Exception raised while creating the host manifest.", ex);
                tw.WriteLine("Error error creating the host manifest:", ex.Message);
                return (int)ExitCode.Error;
            }

            log.Debug("register native messaging host manifest");
            try
            {
                tw.WriteLine("Registering this host:");
                tw.WriteLine("[{0}]", keyName);
                tw.WriteLine("@=\"{0}\"", options.manifest.Replace("\\", "\\\\"));
                Microsoft.Win32.Registry.SetValue(keyName, null, options.manifest);
                log.Debug("manifest registered successfully");
                tw.WriteLine("Host registered successfully");
                tw.WriteLine();
            }
            catch (Exception ex)
            {
                log.Error("Exception raised while registering the host.", ex);
                tw.WriteLine("Error registering the host:", ex.Message);
                return (int)ExitCode.Error;
            }

            return (int)ExitCode.Success;
        }

        /// <summary>
        /// Prints this application usage information.
        /// </summary>
        static int Usage()
        {
            log.Debug("print usage information");
            TextWriter tw = Console.Error;
            tw.WriteLine("Chrome4Net Twain Scan Example Extension. Author Konstantin Kuzvesov, 2015.");
            tw.WriteLine("Usage: {0} [options] <command>", Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location));
            tw.WriteLine();
            tw.WriteLine("Commands with options");
            tw.WriteLine();
            tw.WriteLine("  register                 Register this host.");
            tw.WriteLine("    --hive=<HKCU|HKLM>     The hive to register the host in (default is {0}).", options.hive);
            tw.WriteLine("    --manifest=<file>      The file to output this host manifest to (default is {0}; overwritten, if exists).", options.manifest);
            tw.WriteLine();
            tw.WriteLine("  chrome-extension://*/    Start a native messages relay.");
            tw.WriteLine("    --parent-window=*      Specify parent window id.");
            tw.WriteLine();
            tw.WriteLine("  process                  Start a native messages processor.");
            tw.WriteLine("    --pipe-in=<id>         Specify input pipe, required.");
            tw.WriteLine("    --pipe-out=<id>        Specify output pipe, required.");
            tw.WriteLine();
            return (int)ExitCode.Success;
        }

        /// <summary>
        /// Prints 'invalid command' error.
        /// </summary>
        private static int InvalidCommand(string command)
        {
            TextWriter tw = Console.Error;
            log.ErrorFormat("Invalid command line : unknown command '{0}'.", command);
            tw.WriteLine("Invalid command line : unknown command '{0}'. Start again with no parameters to get usage information.", command);
            return (int)ExitCode.InvalidCommandLine;
        }

        /// <summary>
        /// Prints 'invalid option' error.
        /// </summary>
        private static int InvalidOption(string option)
        {
            TextWriter tw = Console.Error;
            log.ErrorFormat("Invalid command line : unknown option '{0}'.", option);
            tw.WriteLine("Invalid command line : unknown option '{0}'. Start again with no parameters to get usage information.", option);
            return (int)ExitCode.InvalidCommandLine;
        }

        /// <summary>
        ///  Prints 'invalid option value' error.
        /// </summary>
        private static int InvalidOptionValue(string option, string value)
        {
            TextWriter tw = Console.Error;
            log.ErrorFormat("Invalid command line : unknown option '{0}' value '{1}'.", option, value);
            tw.WriteLine("Invalid command line : invalid option '{0}' value '{1}'. Start again with no parameters to get usage information.", option, value);
            return 0;
        }

        /// <summary>
        /// Prints 'required option has no value' error.
        /// </summary>
        static int RequiredOptionHasNoValue(string option)
        {
            TextWriter tw = Console.Error;
            log.ErrorFormat("Invalid command line : required option '{0}' has no value specified.", option);
            tw.WriteLine("Invalid command line : required option '{0}' has no value specified. Start again with no parameters to get usage information.", option);
            return 0;
        }

    }
}
