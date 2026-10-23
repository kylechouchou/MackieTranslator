using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
namespace MackieTranslator {
 public enum CaptureTarget {Unknown,Foxit,Wps,Browser,Word,Other}
 public enum CaptureStep {Idle,Paused,Gesture,ReadSelection,CopyShortcut,CopyMessage,CopyAlternative,Received,Cancelled,NoText,HookError,TranslationStarted,TranslationDone,TranslationError,HistoryLoaded,HistorySaved,HistoryError,ServiceOk,ServiceError}
 public static class LocalDiagnostics {
  static readonly object sync=new object();static readonly Queue<string> events=new Queue<string>();static CaptureStep step=CaptureStep.Idle;static CaptureTarget target;static readonly Dictionary<CaptureTarget,int> received=new Dictionary<CaptureTarget,int>();
  public static void Target(CaptureTarget value){lock(sync)target=value;}
  public static CaptureStep Step {get{lock(sync)return step;}}
  public static void Record(CaptureStep value){Record(value,0);}
  public static void Record(CaptureStep value,int errorCode){lock(sync){step=value;if(value==CaptureStep.Received){int count;received.TryGetValue(target,out count);received[target]=Math.Min(9999,count+1);}if(events.Count==64)events.Dequeue();events.Enqueue(DateTime.Now.ToString("HH:mm:ss")+" "+value+(errorCode==0?"":" code="+errorCode));}}
  public static string Report(bool hookActive,int histories){var b=new StringBuilder();b.AppendLine("MackieTranslator "+BuildInfo.Display);b.AppendLine("仅本地诊断；不包含原文、译文、密钥、文档标题或路径。");b.AppendLine("64-bit process: "+Environment.Is64BitProcess);b.AppendLine("当前阶段: "+Step);b.AppendLine("划词监听: "+(hookActive?"已启用":"未启用"));b.AppendLine("历史条数: "+histories+" / 20");try{b.AppendLine("剪贴板文字格式: "+(Clipboard.ContainsText()?"存在":"不存在"));}catch{b.AppendLine("剪贴板: 暂时不可访问");}try{using(var process=Process.GetCurrentProcess()){process.Refresh();b.AppendLine("工作集 MB: "+(process.WorkingSet64/1048576.0).ToString("F1"));}int count=0;using(var current=Process.GetCurrentProcess()){foreach(var item in Process.GetProcessesByName("MackieTranslator")){using(item){if(item.SessionId==current.SessionId)count++;}}}b.AppendLine("同会话同名程序数: "+count);}catch{b.AppendLine("进程检查: 不可用");}b.AppendLine("网络连接不自动测试；可在翻译服务页点击测试连接（会产生用量）。");lock(sync){b.AppendLine("最近目标软件类别: "+target);b.AppendLine("本次运行取词成功计数（并非所有软件已实测）:");foreach(var item in received)b.AppendLine(item.Key+": "+item.Value);b.AppendLine("最近阶段（最多64条）:");foreach(string item in events)b.AppendLine(item);}return b.ToString();}
  public static string Label(CaptureStep value,bool english){switch(value){case CaptureStep.Gesture:return english?"Selection detected":"已检测到划选";case CaptureStep.ReadSelection:return english?"Reading selection…":"正在读取选中文字…";case CaptureStep.CopyShortcut:case CaptureStep.CopyMessage:case CaptureStep.CopyAlternative:return english?"Reading via copy fallback…":"正在通过复制后备读取选区…";case CaptureStep.Cancelled:return english?"Selection changed; capture cancelled":"选区或焦点已改变，本次取词取消";case CaptureStep.NoText:return english?"No selection text received":"未取到选中文字";case CaptureStep.HookError:return english?"Selection listener unavailable":"划词监听未能启用";default:return "";}}
 }
}
