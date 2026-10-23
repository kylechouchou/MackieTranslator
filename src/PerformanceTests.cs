using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
namespace MackieTranslator {
    public static class PerformanceTests {
        public static readonly Stopwatch Startup=Stopwatch.StartNew();
        static T Field<T>(MainWindow w,string name){return (T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);}
        public static async Task Run(MainWindow w){
            double startup=Startup.Elapsed.TotalMilliseconds;
            var report=new System.Text.StringBuilder();
            Action<string> memory=label=>{using(var p=Process.GetCurrentProcess()){p.Refresh();report.AppendLine(label+" private_MB="+(p.PrivateMemorySize64/1048576.0).ToString("F2")+" working_MB="+(p.WorkingSet64/1048576.0).ToString("F2")+" managed_MB="+(GC.GetTotalMemory(false)/1048576.0).ToString("F2"));}};
            try {
                await Task.Delay(1500);report.AppendLine("startup_loaded_ms="+startup.ToString("F1"));memory("idle");
                using(var p=Process.GetCurrentProcess()){var cpu=p.TotalProcessorTime;var wall=Stopwatch.StartNew();await Task.Delay(4000);p.Refresh();report.AppendLine("idle_cpu_one_core_percent="+((p.TotalProcessorTime-cpu).TotalMilliseconds/wall.Elapsed.TotalMilliseconds*100).ToString("F2"));}
                var pause=Field<System.Windows.Controls.Primitives.ToggleButton>(w,"pause");pause.IsChecked=true;pause.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                using(var p=Process.GetCurrentProcess()){var cpu=p.TotalProcessorTime;var wall=Stopwatch.StartNew();await Task.Delay(2000);p.Refresh();report.AppendLine("paused_cpu_one_core_percent="+((p.TotalProcessorTime-cpu).TotalMilliseconds/wall.Elapsed.TotalMilliseconds*100).ToString("F2"));}
                typeof(MainWindow).GetField("screenshotBusy",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(w,true);
                var output=Field<SpacedTextBox>(w,"output");var timings=new double[200];string chunk="The quick brown fox jumps over the lazy dog. 中文文字用于排版性能检查。 ";
                for(int i=0;i<timings.Length;i++){var watch=Stopwatch.StartNew();output.Text+=chunk+chunk+chunk;w.UpdateLayout();timings[i]=watch.Elapsed.TotalMilliseconds;await Task.Yield();}
                memory("stream_34000_chars");report.AppendLine("stream_update_mean_ms="+timings.Average().ToString("F2"));report.AppendLine("stream_update_p95_ms="+timings.OrderBy(x=>x).ElementAt((int)(timings.Length*.95)).ToString("F2"));
                output.Clear();typeof(MainWindow).GetField("screenshotBusy",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(w,false);
                for(int page=1;page<=4;page++)typeof(MainWindow).GetMethod("ShowPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{page});
                await Task.Delay(300);memory("all_settings_visited");
                var journal=Field<TranslationJournal>(w,"journal");for(int n=0;n<20;n++)journal.Add(new string('a',20000)+n,new string('中',20000)+n,"offline-test");
                var show=typeof(MainWindow).GetMethod("ShowPage",BindingFlags.Instance|BindingFlags.NonPublic);show.Invoke(w,new object[]{5});w.UpdateLayout();show.Invoke(w,new object[]{0});await Task.Delay(100);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();memory("history_cycle_1");
                for(int n=0;n<20;n++){show.Invoke(w,new object[]{5});w.UpdateLayout();show.Invoke(w,new object[]{0});await Task.Delay(15);}GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();memory("history_cycle_21");journal.Clear();

            }catch(Exception error){report.AppendLine("FAILED: "+error);}
            finally {File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"performance-test-result.txt"),report.ToString());w.Close();}
        }
    }
}
