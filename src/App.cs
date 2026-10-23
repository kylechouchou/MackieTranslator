using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
[assembly: AssemblyTitle("MackieTranslator")]
[assembly: AssemblyProduct("MackieTranslator")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8",FrameworkDisplayName=".NET Framework 4.8")]
namespace MackieTranslator {
    static class App {
#if DIAGNOSTICS
        static bool shutdownProbe;
#endif
        [System.Diagnostics.Conditional("DIAGNOSTICS")]
        internal static void ProbeTrace(string message){
#if DIAGNOSTICS
            if(shutdownProbe)System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shutdown-probe-progress.txt"),DateTime.UtcNow.ToString("O")+" "+message+Environment.NewLine);
#endif
        }

        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        [STAThread]static int Main(string[] args) {
#if DIAGNOSTICS
            if(Array.IndexOf(args,"--perf-test")>=0)PerformanceTests.Startup.Start();
            shutdownProbe=Array.IndexOf(args,"--shutdown-probe")>=0;if(shutdownProbe)System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shutdown-probe-progress.txt"),"Starting probe\n");
#endif
            System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.SystemDefault;
#if DIAGNOSTICS
            if(args.Length>0&&args[0]=="--reliability-test")return ReliabilityTests.Run();if(args.Length>0&&args[0]=="--language-test")return ApiTests.Run(false);if(args.Length>0&&args[0]=="--multilingual-ocr-test")return OcrTests.RunMultilingual();if(args.Length>0&&args[0]=="--self-test")return SelfTests.Run();if(args.Length>0&&args[0]=="--api-test")return ApiTests.Run();if(args.Length>0&&args[0]=="--ocr-test")return OcrTests.Run();if(args.Length>0&&args[0]=="--optimization-test")return OptimizationTests.Run();
#endif
            SetCurrentProcessExplicitAppUserModelID("MackieTranslator.WPF");
            // The small 2D interface avoids loading a large graphics-driver allocation by default.
            RenderOptions.ProcessRenderMode=Array.IndexOf(args,"--hardware-render")>=0
                ?System.Windows.Interop.RenderMode.Default:System.Windows.Interop.RenderMode.SoftwareOnly;
            var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
            try {
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MackieTranslator.Styles.xaml"))app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
                ProbeTrace("Application resources loaded");var settings=new Settings();
#if DIAGNOSTICS
                if(Array.IndexOf(args,"--capture-test")>=0||System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"capture-test.flag"))){settings.Values["automatic"]=false;}else settings.Load();
#else
                settings.Load();
#endif
ProbeTrace("Settings loaded");
                var window=new MainWindow(settings);ProbeTrace("Main window constructed");
                app.MainWindow=window;
                TextOptions.SetTextFormattingMode(window,TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(window,TextRenderingMode.Auto);
#if DIAGNOSTICS
                GuiTests.LayoutOnly=Array.IndexOf(args,"--layout-only")>=0;bool diagnosticStarted=false;
                if(args.Length>0&&args[0]=="--perf-test")window.Loaded+=async(s,e)=>{if(diagnosticStarted)return;diagnosticStarted=true;await PerformanceTests.Run(window);};
                if(args.Length>0&&args[0]=="--ui-test")window.Loaded+=async(s,e)=>{if(diagnosticStarted)return;diagnosticStarted=true;await GuiTests.Run(window);};
                if(args.Length>0&&args[0]=="--shutdown-probe")window.Loaded+=async(s,e)=>{if(diagnosticStarted)return;diagnosticStarted=true;ProbeTrace("Probe loaded");await System.Threading.Tasks.Task.Delay(150);var flags=BindingFlags.Instance|BindingFlags.NonPublic;string scenario=args.Length>1?args[1]:"idle";if(scenario=="typing"){var editor=(SpacedTextBox)typeof(MainWindow).GetField("source",flags).GetValue(window);editor.Text="Closing before automatic translation starts.";}else if(scenario=="network"){var editor=(SpacedTextBox)typeof(MainWindow).GetField("source",flags).GetValue(window);editor.Text="Hello";typeof(MainWindow).GetMethod("Translate",flags).Invoke(window,null);await System.Threading.Tasks.Task.Delay(20);}else if(scenario=="popup"){typeof(MainWindow).GetMethod("ShowPage",flags).Invoke(window,new object[]{4});((System.Windows.Controls.ComboBox)typeof(MainWindow).GetField("provider",flags).GetValue(window)).IsDropDownOpen=true;}else if(scenario=="screenshot"){typeof(MainWindow).GetMethod("StartScreenshot",flags).Invoke(window,null);await System.Threading.Tasks.Task.Delay(200);}else if(scenario=="capture-cycle"){var pause=(System.Windows.Controls.Primitives.ToggleButton)typeof(MainWindow).GetField("pause",flags).GetValue(window);for(int i=0;i<20;i++){pause.IsChecked=i%2==0;pause.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}}else if(scenario=="fonts"){typeof(MainWindow).GetMethod("ShowPage",flags).Invoke(window,new object[]{1});}ProbeTrace("Closing window");window.Close();ProbeTrace("Window.Close returned");};
#endif
                app.Run(window);ProbeTrace("Application exited");
#if DIAGNOSTICS
                return GuiTests.Failed?1:0;
#else
                return 0;
#endif
            }catch(Exception error){
#if DIAGNOSTICS
                if(shutdownProbe){ProbeTrace("FAILED: "+error);return 1;}if(Array.IndexOf(args,"--ui-test")>=0){System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-result.txt"),"STARTUP FAILED: "+error);return 1;}
#endif
                MessageBox.Show("MackieTranslator 无法启动 / Could not start.\n"+error.Message,"MackieTranslator",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
        }
    }
}



