using JitLogParser;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TestApplication;
using static JitLogParser.MethodBaseSerializer;

namespace JitProfilerController
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        Process p;
        IpcFlagMap ipcFlagMap;
        string logFolder;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            //Collect(@"C:\siglocal\JitProfilerPlugin\20251118_200121");
            //return;

            if (btLaunchKill.Content.ToString() == "Launch")
            {
                var runPrefix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                //logFolder = System.IO.Path.Combine(OutFolder.Text, runPrefix);
                logFolder = OutFolder.Text;

                if (!Directory.Exists(logFolder))
                    Directory.CreateDirectory(logFolder);
                Title = $"SIG JIT PROFILER - {runPrefix}";
                ipcFlagMap = new IpcFlagMap();
                var procInfo = new ProcessStartInfo()
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    CreateNoWindow = false,
                    FileName = TargetExec.Text,
                    Arguments = TargetExecArgs.Text,
                };
                procInfo.EnvironmentVariables.Add("CORECLR_PROFILER", "{DF9EDC4B-25C1-4925-A3FB-6AAEB3E2FACD}");
                procInfo.EnvironmentVariables.Add("CORECLR_PROFILER_PATH", @".\JitProfilerPlugin.dll");
                procInfo.EnvironmentVariables.Add("CORECLR_ENABLE_PROFILING", "1");
                procInfo.EnvironmentVariables.Add("DOTNET_EnableDiagnostics", "1");
                procInfo.EnvironmentVariables.Add("DOTNET_EnableDiagnostics_Profiler", "1");
                procInfo.EnvironmentVariables.Add("SIG_JIT_PROFILER_LOG_PATH", logFolder);
                procInfo.EnvironmentVariables.Add("SIG_JIT_PROFILER_MAP_ID", IpcFlagMap.DefaultJitProfilerId);
                p = Process.Start(procInfo);
                p.EnableRaisingEvents = true;
                p.Exited += P_Exited;

                ProfileControl.IsEnabled = true;
                TargetExec.IsEnabled = false;
                btLaunchKill.Content = "Collect";
            }
            else
            {
                if (!p.HasExited)
                {
                    ipcFlagMap.SetFlag(0);
                    Thread.Sleep(1000);
                    p.Kill(true);
                }
                else
                {
                    HandleKill();
                }
            }
        }

        private void HandleKill()
        {
            ProfileControl.IsEnabled = false;
            ProfileControl.Content = "Start Profiling";
            btLaunchKill.Content = "Launch";
            Collect(logFolder);
        }

        private void Collect(string folder)
        {
            var path = System.IO.Path.GetDirectoryName(TargetExec.Text);
            var methods = JitProfilerLogParser.ParseProfilerLogs(
               System.IO.Path.Combine(folder, "jit.json"),
               System.IO.Path.Combine(folder, "modules.json"),
               System.IO.Path.Combine(folder, "enter3.json"),
               path,
               out string erros
               );
            output.Text = string.Join("\r\n", methods.Select(x => x.ToPrettySignature()));
            errorLog.Text = erros;
            using (var tw = File.AppendText(System.IO.Path.Combine(folder, "jitManifest.json")))
            {
                tw.WriteLine("[");
                bool started = false;
                foreach (var method in methods)
                {
                    var json = MethodBaseSerializer.Serialize(method);
                    if (started)
                        tw.WriteLine(",");
                    started = true;
                    tw.WriteLine(json);
                }
                tw.WriteLine("]");
            }
            TargetExec.IsEnabled = true;
        }

        private void P_Exited(object sender, System.EventArgs e)
        {
            this.Dispatcher.Invoke(HandleKill);
        }

        private void ProfileControl_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileControl.Content.ToString() == "Stop Profiling")
            {
                ipcFlagMap.SetFlag(0);
                ProfileControl.Content = "Start Profiling";
            }
            else
            {
                ipcFlagMap.SetFlag(1);
                ProfileControl.Content = "Stop Profiling";
            }
        }

    }
}