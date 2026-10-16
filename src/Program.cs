using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Battery24GTray
{
    internal static class Program
    {
        internal static readonly string LogPath = Path.Combine(Path.GetTempPath(), "Battery24GTray.log");

        [STAThread]
        private static void Main(string[] args)
        {
            AppendLog("\n===== START =====");
            AppendLog("args: " + string.Join(", ", args));
            Console.Error.WriteLine("[Battery24GTray] Starting... args=" + string.Join(", ", args));

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = (Exception)e.ExceptionObject;
                AppendLog("[UNHANDLED] " + ex);
                Console.Error.WriteLine("[UNHANDLED] " + ex);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppendLog("[THREAD] " + e.Exception);
                Console.Error.WriteLine("[THREAD] " + e.Exception);
            };

            bool demo = HasFlag(args, "--demo");
            int selfTest = GetIntFlag(args, "--selftest");
            AppendLog("demo=" + demo + " selfTest=" + selfTest);
            Console.Error.WriteLine("demo=" + demo + " selfTest=" + selfTest);

            try
            {
                bool createdNew;
                Mutex mutex = new Mutex(true, @"Global\Battery24GTray_SingleInstance", out createdNew);
                AppendLog("mutex createdNew=" + createdNew);
                Console.Error.WriteLine("mutex createdNew=" + createdNew);

                if (!createdNew)
                {
                    AppendLog("EXISTS: another instance holds mutex, exiting");
                    Console.Error.WriteLine("Another instance is running, exiting.");
                    return;
                }

                AppendLog("entering Application.Run...");
                Console.Error.WriteLine("Starting WinForms message loop...");
                Application.EnableVisualStyles();
                AppendLog("after EnableVisualStyles");
                Application.SetCompatibleTextRenderingDefault(false);
                AppendLog("after SetCompatibleTextRenderingDefault");

                var ctx = new TrayApplicationContext(demo, selfTest);
                AppendLog("after new TrayApplicationContext");
                Console.Error.WriteLine("TrayApplicationContext created, entering message loop.");

                // Initialize() must run on the UI thread AFTER the message pump starts,
                // because WinForms controls (ContextMenuStrip, NotifyIcon) require
                // SynchronizationContext to be set.  Use Application.Idle to fire once
                // the message loop has begun.
                var initFired = new System.Threading.AutoResetEvent(false);
                Exception initException = null;
                Application.Idle += delegate
                {
                    Application.Idle -= (EventHandler)null; // remove handler
                    try
                    {
                        ctx.Initialize();
                        initFired.Set();
                    }
                    catch (Exception ex)
                    {
                        initException = ex;
                        initFired.Set();
                    }
                };

                Application.Run(ctx);
                AppendLog("after Application.Run (exit)");
                Console.Error.WriteLine("Application.Run returned.");

                initFired.WaitOne(5000);
                if (initException != null)
                {
                    AppendLog("[FATAL] Initialize() threw: " + initException);
                    Console.Error.WriteLine("[FATAL] Initialize(): " + initException);
                }
            }
            catch (Exception ex)
            {
                AppendLog("[FATAL] " + ex);
                Console.Error.WriteLine("[FATAL] " + ex);
            }
        }

        internal static readonly object _logLock = new object();

        internal static void AppendLog(string line)
        {
            try
            {
                lock (_logLock)
                using (var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var sw = new StreamWriter(fs))
                    sw.WriteLine(line);
            }
            catch { }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int GetIntFlag(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int v;
                    if (int.TryParse(args[i + 1], out v)) return v;
                }
                if (args[i] != null && args[i].StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
                {
                    int v2;
                    if (int.TryParse(args[i].Substring(flag.Length + 1), out v2)) return v2;
                }
            }
            return 0;
        }
    }
}
