using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PixelFormats=System.Windows.Media.PixelFormats;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;
namespace MackieTranslator {
    public sealed class ScreenshotSelection:IDisposable {
        readonly List<SelectionOverlay> overlays=new List<SelectionOverlay>();
        readonly TaskCompletionSource<Rectangle?> completion=new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly Rectangle desktop;
        Point origin,current;bool dragging,finished,ready;
        CancellationTokenRegistration registration;
        public ScreenshotSelection(){desktop=Forms.SystemInformation.VirtualScreen;}
        public Task<Rectangle?> Start(CancellationToken token){
            token.ThrowIfCancellationRequested();var context=SynchronizationContext.Current;
            try {foreach(var screen in Forms.Screen.AllScreens){var overlay=new SelectionOverlay(this,screen.Bounds);overlays.Add(overlay);overlay.Show();}ready=true;var focus=overlays.FirstOrDefault(x=>x.Bounds.Contains(Forms.Cursor.Position))??overlays.FirstOrDefault();focus?.Activate();}
            catch {Cancel();throw;}finally{SynchronizationContext.SetSynchronizationContext(context);}
            registration=token.Register(()=>System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(Cancel)));
            return completion.Task;
        }
        internal void Begin(SelectionOverlay overlay){if(finished)return;dragging=true;origin=current=Forms.Cursor.Position;overlay.Capture=true;Redraw();}
        internal void Move(){if(finished||!dragging)return;current=Forms.Cursor.Position;Redraw();}
        internal void End(){if(finished||!dragging)return;current=Forms.Cursor.Position;var area=Normalize(origin,current,desktop);Complete(area.Width>=5&&area.Height>=5?(Rectangle?)area:null);}
        internal void LostCapture(){if(!finished&&dragging)Cancel();}
        internal void Deactivated(){if(!ready||dragging||finished)return;System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(()=>{if(ready&&!dragging&&!finished&&!overlays.Any(x=>x.ContainsFocus))Cancel();}));}
        internal Rectangle? CurrentArea{get{return dragging?(Rectangle?)Normalize(origin,current,desktop):null;}}
        void Redraw(){foreach(var overlay in overlays)overlay.Invalidate();}
        public void Cancel(){Complete(null);}
        void Complete(Rectangle? area){if(finished)return;finished=true;dragging=false;foreach(var overlay in overlays){overlay.Capture=false;overlay.Close();overlay.Dispose();}overlays.Clear();completion.TrySetResult(area);}
        public void Dispose(){Cancel();registration.Dispose();}
        public static Rectangle Normalize(Point a,Point b,Rectangle desktop){int left=Math.Max(desktop.Left,Math.Min(a.X,b.X)),top=Math.Max(desktop.Top,Math.Min(a.Y,b.Y));int right=Math.Min(desktop.Right,Math.Max(a.X,b.X)),bottom=Math.Min(desktop.Bottom,Math.Max(a.Y,b.Y));return new Rectangle(left,top,Math.Max(0,right-left),Math.Max(0,bottom-top));}
        internal static Size OcrSize(Size original,int maximum){if(original.Width<=maximum&&original.Height<=maximum)return original;double ratio=(double)maximum/Math.Max(original.Width,original.Height);return new Size(Math.Max(1,(int)Math.Floor(original.Width*ratio)),Math.Max(1,(int)Math.Floor(original.Height*ratio)));}
        public static BitmapSource Capture(Rectangle area){
            if(area.Width<5||area.Height<5)throw new InvalidOperationException("截图区域太小。 / Selected area is too small.");
            if((long)area.Width*area.Height>24000000)throw new InvalidOperationException("截图范围过大，请缩小到需要翻译的文字。 / Select a smaller text region.");
            using(var bitmap=new Bitmap(area.Width,area.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){
                using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(area.Location,Point.Empty,area.Size,CopyPixelOperation.SourceCopy);
                int maximum=(int)Windows.Media.Ocr.OcrEngine.MaxImageDimension;var size=OcrSize(area.Size,maximum);
                using(var scaled=size!=area.Size?new Bitmap(size.Width,size.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb):null){
                    var working=scaled??bitmap;
                    if(scaled!=null)using(var graphics=Graphics.FromImage(scaled)){graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;graphics.DrawImage(bitmap,new Rectangle(Point.Empty,size));}
                    var data=working.LockBits(new Rectangle(0,0,working.Width,working.Height),ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try {int stride=checked(working.Width*4);var pixels=new byte[checked(stride*working.Height)];for(int row=0;row<working.Height;row++)Marshal.Copy(IntPtr.Add(data.Scan0,row*data.Stride),pixels,row*stride,stride);var result=BitmapSource.Create(working.Width,working.Height,96,96,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();return result;}
                    finally{working.UnlockBits(data);}
                }
            }
        }
    }
    internal sealed class SelectionOverlay:Forms.Form {
        readonly ScreenshotSelection session;
        public SelectionOverlay(ScreenshotSelection owner,Rectangle bounds){session=owner;AutoScaleMode=Forms.AutoScaleMode.None;FormBorderStyle=Forms.FormBorderStyle.None;StartPosition=Forms.FormStartPosition.Manual;Bounds=bounds;ShowInTaskbar=false;TopMost=true;BackColor=Color.Black;TransparencyKey=Color.Magenta;Opacity=.55;Cursor=Forms.Cursors.Cross;KeyPreview=true;DoubleBuffered=true;Text="MackieTranslator 截图翻译";
            KeyDown+=(s,e)=>{if(e.KeyCode==Forms.Keys.Escape){e.Handled=true;session.Cancel();}};
            MouseDown+=(s,e)=>{if(e.Button==Forms.MouseButtons.Right)session.Cancel();else if(e.Button==Forms.MouseButtons.Left)session.Begin(this);};MouseMove+=(s,e)=>session.Move();MouseUp+=(s,e)=>{if(e.Button==Forms.MouseButtons.Left)session.End();};MouseCaptureChanged+=(s,e)=>{if(!Capture)session.LostCapture();};FormClosed+=(s,e)=>session.Cancel();Deactivate+=(s,e)=>session.Deactivated();
        }
        protected override void OnPaint(Forms.PaintEventArgs e){base.OnPaint(e);using(var font=new Font("Microsoft YaHei UI",12))e.Graphics.DrawString("拖动框选文字 · 松开识别 · Esc / 右键取消",font,Brushes.White,new PointF(24,24));if(session.CurrentArea is Rectangle region){region.Offset(-Bounds.Left,-Bounds.Top);using(var clear=new SolidBrush(Color.Magenta))e.Graphics.FillRectangle(clear,region);using(var edge=new Pen(Color.White,2))e.Graphics.DrawRectangle(edge,region);}}
    }
}

