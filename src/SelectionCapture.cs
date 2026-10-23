using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
namespace MackieTranslator {
 public sealed class SelectionCapture:IDisposable {
  [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)] struct MouseData {public Point Point;public uint Data,Flags,Time;public IntPtr Extra;}
  delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
  [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
  [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
  internal static IntPtr TargetAtScreenPoint(int x,int y){var child=WindowFromPoint(new Point{X=x,Y=y});if(child==IntPtr.Zero)return IntPtr.Zero;var root=GetAncestor(child,2);return root==IntPtr.Zero?child:root;}

  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
  [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
  [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct GuiInfo {public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rect CaretRect;}
  [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
  [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wparam,IntPtr lparam,uint flags,uint timeout,out UIntPtr result);
  [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
  [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
  static readonly uint ownPid=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
  readonly Dispatcher dispatcher;readonly HookProc callback;IntPtr hook,downWindow,lastWindow;Point down,last;long lastUp;int generation,reading;volatile bool enabled;Dispatcher hookDispatcher;Thread hookThread;bool disposed;string queued="",notice="";
  public SelectionCapture(){dispatcher=Dispatcher.CurrentDispatcher;callback=MouseHook;}
  void EnsureHookThread(){if(hookDispatcher!=null)return;using(var ready=new ManualResetEventSlim(false)){
   hookThread=new Thread(()=>{hookDispatcher=Dispatcher.CurrentDispatcher;ready.Set();Dispatcher.Run();}){IsBackground=true,Name="Mackie selection hook"};hookThread.SetApartmentState(ApartmentState.STA);hookThread.Start();ready.Wait();
  }}
  public void SetEnabled(bool value){if(disposed)return;Interlocked.Increment(ref generation);Stage(value?CaptureStep.Idle:CaptureStep.Paused);enabled=value;queued="";notice="";downWindow=IntPtr.Zero;if(value&&hook==IntPtr.Zero){EnsureHookThread();hook=hookDispatcher.Invoke(()=>SetWindowsHookEx(14,callback,GetModuleHandle(null),0));if(hook==IntPtr.Zero){enabled=false;Stage(CaptureStep.HookError);throw new InvalidOperationException("无法启用划词鼠标监听，请重启软件。 / Mouse hook unavailable.");}}else if(!value&&hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
  public bool IsEnabled {get{return enabled&&hook!=IntPtr.Zero;}}
  CaptureStep? pendingStep;
  void Stage(CaptureStep value){pendingStep=value;LocalDiagnostics.Record(value);}
  public CaptureStep? PollStage(){var value=pendingStep;pendingStep=null;return value;}
  public string PollNotice(){string value=notice;notice="";return value;}
  public string Poll(){string text=queued;queued="";return text;}
  [System.Diagnostics.Conditional("DIAGNOSTICS")]
  static void Trace(string message){try{string root=AppDomain.CurrentDomain.BaseDirectory;if(!System.IO.File.Exists(System.IO.Path.Combine(root,"capture-test.flag")))return;string path=System.IO.Path.Combine(root,"capture-trace.txt");if(System.IO.File.Exists(path)&&new System.IO.FileInfo(path).Length>64000)System.IO.File.WriteAllText(path,"");System.IO.File.AppendAllText(path,DateTime.Now.ToString("HH:mm:ss.fff")+" "+message+"\n");}catch{}}
  static CaptureTarget IdentifyTarget(IntPtr window){try{uint pid;GetWindowThreadProcessId(window,out pid);using(var process=System.Diagnostics.Process.GetProcessById((int)pid)){string name=process.ProcessName.ToLowerInvariant();return name.Contains("foxit")?CaptureTarget.Foxit:name.Contains("wps")?CaptureTarget.Wps:name=="winword"?CaptureTarget.Word:name=="chrome"||name=="msedge"||name=="firefox"?CaptureTarget.Browser:CaptureTarget.Other;}}catch{return CaptureTarget.Unknown;}}
  static bool IsFoxit(IntPtr window){try{uint pid;GetWindowThreadProcessId(window,out pid);using(var process=System.Diagnostics.Process.GetProcessById((int)pid))return process.ProcessName.IndexOf("Foxit",StringComparison.OrdinalIgnoreCase)>=0;}catch{return false;}}
  static bool SameTarget(IntPtr original,IntPtr current){if(original==IntPtr.Zero||current==IntPtr.Zero)return false;if(original==current)return true;uint first,second;GetWindowThreadProcessId(original,out first);GetWindowThreadProcessId(current,out second);return first!=0&&first==second&&IsFoxit(original);}
  static bool Eligible(IntPtr window){uint pid;GetWindowThreadProcessId(window,out pid);if(window==IntPtr.Zero||pid==0||pid==ownPid)return false;var name=new StringBuilder(256);GetClassName(window,name,256);string value=name.ToString();return value!="CabinetWClass"&&value!="ExploreWClass"&&value!="Progman"&&value!="WorkerW"&&value!="Shell_TrayWnd"&&value!="ConsoleWindowClass"&&value!="CASCADIA_HOSTING_WINDOW_CLASS";}
  public static bool CanContinue(bool active,int current,int requested,bool sameTarget){return active&&current==requested&&sameTarget;}
  public static bool IsSelectionGesture(int dx,int dy,bool doubleClick){return (long)dx*dx+(long)dy*dy>=16||doubleClick;}
  IntPtr MouseHook(int code,IntPtr message,IntPtr data){
   // A global low-level hook must return promptly: never query another process here.
   try{if(code>=0&&enabled){int msg=message.ToInt32();if(msg==0x201){var mouse=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));Interlocked.Increment(ref generation);down=mouse.Point;downWindow=TargetAtScreenPoint(mouse.Point.X,mouse.Point.Y);}
    else if(msg==0x202){var mouse=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));var window=TargetAtScreenPoint(mouse.Point.X,mouse.Point.Y);var started=downWindow;int dx=mouse.Point.X-down.X,dy=mouse.Point.Y-down.Y;long now=Environment.TickCount&0x7fffffff;bool twice=lastWindow==window&&now>=lastUp&&now-lastUp<=GetDoubleClickTime()&&Math.Abs(mouse.Point.X-last.X)<5&&Math.Abs(mouse.Point.Y-last.Y)<5;lastUp=now;last=mouse.Point;lastWindow=window;downWindow=IntPtr.Zero;
     if(IsSelectionGesture(dx,dy,twice)){int ticket=generation;dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!enabled||ticket!=generation||!SameTarget(started,window)||!Eligible(window))return;Stage(CaptureStep.Gesture);Capture(window,ticket);}));}
    }}
   }catch{}return CallNextHookEx(hook,code,message,data);
  }
  bool Current(IntPtr window,int ticket){bool result=CanContinue(enabled,generation,ticket,SameTarget(window,GetForegroundWindow()));if(enabled&&!result)Stage(CaptureStep.Cancelled);return result;}
  static bool Modifiers(){return GetAsyncKeyState(0x10)<0||GetAsyncKeyState(0x11)<0||GetAsyncKeyState(0x12)<0||GetAsyncKeyState(0x5b)<0||GetAsyncKeyState(0x5c)<0;}
  static string SelectedText(uint expectedPid){try{var element=AutomationElement.FocusedElement;if(element==null||element.Current.ProcessId!=(int)expectedPid||element.Current.IsPassword)return "";object value;if(!element.TryGetCurrentPattern(TextPattern.Pattern,out value))return "";var ranges=((TextPattern)value).GetSelection();if(ranges.Length!=1)return "";return ranges[0].GetText(60000)??"";}catch{return "";}}
  async void Capture(IntPtr window,int ticket){try{Trace("capture scheduled");LocalDiagnostics.Target(IdentifyTarget(window));await Task.Delay(220);Trace("stable current="+Current(window,ticket)+" modifiers="+Modifiers());if(!Current(window,ticket)||Modifiers())return;
   Stage(CaptureStep.ReadSelection);if(Interlocked.CompareExchange(ref reading,1,0)==0){uint selectedPid;GetWindowThreadProcessId(window,out selectedPid);var task=Task.Run(()=>{try{return SelectedText(selectedPid);}finally{Interlocked.Exchange(ref reading,0);}});if(await Task.WhenAny(task,Task.Delay(350))==task){string direct=await task;if(!Current(window,ticket))return;if(!string.IsNullOrWhiteSpace(direct)){queued=direct;Stage(CaptureStep.Received);return;}}}
   Trace("UIA empty or timeout");if(!Current(window,ticket)||Modifiers())return;bool foxit=IsFoxit(window);uint before=GetClipboardSequenceNumber();
   if(!await CopyShortcut(window,ticket,0x43))return;
   if(await ReadCopied(window,ticket,before,16))return;
   if(foxit&&Current(window,ticket)&&!Modifiers()){
    // Some Foxit versions route selection and floating toolbar focus to different windows.
    Stage(CaptureStep.CopyMessage);before=GetClipboardSequenceNumber();var info=new GuiInfo{Size=(uint)Marshal.SizeOf(typeof(GuiInfo))};uint pid;uint thread=GetWindowThreadProcessId(GetForegroundWindow(),out pid);UIntPtr result;
    if(GetGUIThreadInfo(thread,ref info)&&info.Focus!=IntPtr.Zero){uint focusPid;GetWindowThreadProcessId(info.Focus,out focusPid);if(focusPid==pid&&SendMessageTimeout(info.Focus,0x301,IntPtr.Zero,IntPtr.Zero,2,150,out result)==IntPtr.Zero)LocalDiagnostics.Record(CaptureStep.CopyMessage,Marshal.GetLastWin32Error());}
    if(await ReadCopied(window,ticket,before,8))return;
    if(!Current(window,ticket)||Modifiers())return;before=GetClipboardSequenceNumber();if(!await CopyShortcut(window,ticket,0x2d))return;
    if(await ReadCopied(window,ticket,before,20))return;
   }
   if(Current(window,ticket)){Stage(CaptureStep.NoText);notice=foxit?"福昕取词失败：选区接口无文本，Ctrl+C、复制消息和 Ctrl+Insert 均未取到新文字。请确认福昕与本软件权限一致。":"划词未读取到文字，请检查文档的复制权限以及程序权限。";}
  }catch(Exception error){if(Current(window,ticket))notice="划词采集失败："+error.GetType().Name;} }
  async Task<bool> CopyShortcut(IntPtr window,int ticket,byte keyCode){if(!Current(window,ticket)||Modifiers())return false;Stage(keyCode==0x43?CaptureStep.CopyShortcut:CaptureStep.CopyAlternative);bool control=false,key=false;try{keybd_event(0x11,0,0,UIntPtr.Zero);control=true;await Task.Delay(50);if(!Current(window,ticket))return false;keybd_event(keyCode,0,0,UIntPtr.Zero);key=true;await Task.Delay(50);return true;}finally{if(key)keybd_event(keyCode,0,2,UIntPtr.Zero);if(control)keybd_event(0x11,0,2,UIntPtr.Zero);}}
  async Task<bool> ReadCopied(IntPtr window,int ticket,uint before,int attempts){for(int attempt=0;attempt<attempts&&Current(window,ticket);attempt++){await Task.Delay(50);if(attempt==0)Trace("clipboard changed="+(GetClipboardSequenceNumber()!=before));if(GetClipboardSequenceNumber()==before)continue;try{if(Clipboard.ContainsText()){string text=Clipboard.GetText();if(!string.IsNullOrWhiteSpace(text)&&text.Length<=60000){queued=text;Stage(CaptureStep.Received);Trace("clipboard received chars="+text.Length);return true;}}}catch(ExternalException){}}return false;}
  public void Dispose(){if(disposed)return;SetEnabled(false);disposed=true;if(hookDispatcher!=null)hookDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);}
 }
}
