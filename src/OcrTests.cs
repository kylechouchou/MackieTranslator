using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using Windows.Media.Ocr;
using System.Windows.Media.Imaging;
namespace MackieTranslator {
    public static class OcrTests {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static BitmapSource Sample(string first="Screenshot translation test 123",string second="截图翻译功能测试"){using(var bitmap=new Bitmap(1000,220,System.Drawing.Imaging.PixelFormat.Format32bppArgb)){using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",26,FontStyle.Regular)){g.Clear(System.Drawing.Color.White);g.DrawString(first,font,System.Drawing.Brushes.Black,new PointF(24,24));g.DrawString(second,font,System.Drawing.Brushes.Black,new PointF(24,100));}var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);try{var pixels=new byte[bitmap.Width*bitmap.Height*4];for(int y=0;y<bitmap.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*bitmap.Width*4,bitmap.Width*4);var image=BitmapSource.Create(bitmap.Width,bitmap.Height,96,96,PixelFormats.Bgra32,null,pixels,bitmap.Width*4);image.Freeze();return image;}finally{bitmap.UnlockBits(data);}}}
        public static int RunMultilingual(){try{
            var log=new System.Text.StringBuilder("Installed OCR languages: "+string.Join(", ",ScreenshotOcr.Languages())+"\n");
            foreach(var sample in new[]{new[]{"Guten Morgen", "Über die Straße", "Guten", "Morgen"},new[]{"Buongiorno mondo", "La città è bella", "Buongiorno", "mondo"},new[]{"你好世界", "中文翻译测试", "你好", "世界"}}){
                string recognized=ScreenshotOcr.RecognizeAsync(Sample(sample[0],sample[1]),CancellationToken.None).GetAwaiter().GetResult();log.AppendLine("auto: "+recognized);
                Check(recognized.Contains(sample[2])&&recognized.Contains(sample[3]),"Multilingual OCR: "+recognized);
                Check(TextTools.DetectDirection(recognized)==(sample[0].Contains("你好")?"中译英":"外译中"),"Screenshot direction follows recognized language");
            }
            foreach(string tag in ScreenshotOcr.Languages()){
                string recognized=ScreenshotOcr.RecognizeAsync(Sample("Guten Morgen", "Buongiorno mondo"),CancellationToken.None,tag).GetAwaiter().GetResult();log.AppendLine(tag+": "+recognized);Check(!string.IsNullOrWhiteSpace(recognized),"Selected OCR engine works: "+tag);
            }
            foreach(var api in ApiServices.All){
                var selected=new Settings{Key="offline-vision-key"};selected.Values["provider"]=api.Name;selected.Values["base_url"]=api.BaseUrl;selected.Values["model"]=api.Models[0];
                const string response="{\"choices\":[{\"message\":{\"content\":\"ฉันรักคุณ\"},\"finish_reason\":\"stop\"}]}";
                bool sent=false;string result=ScreenshotOcr.RecognizeWithServiceAsync(Sample("Guten Morgen","Buongiorno"),selected,CancellationToken.None,new ApiTests.Mock(response,(request,json)=>{sent=true;Check(json.Contains("data:image/png;base64,iVBOR"),api.Name+" real PNG encoded");})).GetAwaiter().GetResult();
                Check(sent&&result=="ฉันรักคุณ",api.Name+" screenshot reaches its AI service and returns Thai");
                bool repeated=false;string cached=ScreenshotOcr.RecognizeWithServiceAsync(Sample("Guten Morgen","Buongiorno"),selected,CancellationToken.None,new ApiTests.Mock(response,(request,json)=>repeated=true)).GetAwaiter().GetResult();Check(!repeated&&cached==result,api.Name+" identical screenshot reuses bounded in-memory cache");
            }
            bool unavailable=false;try{ScreenshotOcr.RecognizeAsync(Sample(),CancellationToken.None,"xx-unavailable").GetAwaiter().GetResult();}catch(InvalidOperationException){unavailable=true;}Check(unavailable,"Missing OCR language gives actionable error");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"multilingual-ocr-test-result.txt"),count+" multilingual OCR checks passed.\n"+log);return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"multilingual-ocr-test-result.txt"),"FAILED: "+error);return 1;}}
        public static int Run(){try{
            var desktop=new Rectangle(-1920,-200,3840,1280);Check(ScreenshotSelection.Normalize(new System.Drawing.Point(200,100),new System.Drawing.Point(-500,-100),desktop)==new Rectangle(-500,-100,700,200),"Reverse drag and negative monitor coordinates");Check(ScreenshotSelection.Normalize(new System.Drawing.Point(-3000,-500),new System.Drawing.Point(5000,3000),desktop)==desktop,"Selection clamped to desktop");Check(ScreenshotOcr.CleanText("截 图 翻 译\nHello world")=="截图翻译\nHello world","Chinese OCR gaps repaired, English spacing retained");Check(ScreenshotOcr.Languages().Length>0,"Windows OCR languages available");
            var image=Sample();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ocr-test-fixture.png")))encoder.Save(file);var result=ScreenshotOcr.RecognizeAsync(image,CancellationToken.None).GetAwaiter().GetResult();Check(result.IndexOf("Screenshot",StringComparison.OrdinalIgnoreCase)>=0&&result.Replace(" ","").Contains("123"),"Generated English image recognized: "+result);Check(result.Contains("截图翻译"),"Generated Chinese image recognized: "+result);
            var wide=new TransformedBitmap(image,new ScaleTransform(((double)OcrEngine.MaxImageDimension+200)/image.PixelWidth,((double)OcrEngine.MaxImageDimension+200)/image.PixelWidth));wide.Freeze();Check(wide.PixelWidth>OcrEngine.MaxImageDimension,"Oversized fixture exercises OCR resizing");Check(ScreenshotOcr.RecognizeAsync(wide,CancellationToken.None).GetAwaiter().GetResult().IndexOf("translation",StringComparison.OrdinalIgnoreCase)>=0,"Oversized image scaled and recognized");
            var white=new byte[200*100*4];for(int i=0;i<white.Length;i++)white[i]=255;var blank=BitmapSource.Create(200,100,96,96,PixelFormats.Bgra32,null,white,200*4);blank.Freeze();Check(ScreenshotOcr.RecognizeAsync(blank,CancellationToken.None).GetAwaiter().GetResult()=="","Blank image produces no false text");
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool stopped=false;try{ScreenshotOcr.RecognizeAsync(image,cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}Check(stopped,"OCR cancellation respected");}
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ocr-test-result.txt"),count+" OCR checks passed.\nGenerated English/Chinese fixture recognized locally. No screen image captured or uploaded.\nInstalled OCR languages: "+string.Join(", ",ScreenshotOcr.Languages())+"\n");return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ocr-test-result.txt"),"FAILED: "+error);return 1;}}
    }
}


