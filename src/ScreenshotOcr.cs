using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
namespace MackieTranslator {
    public static class ScreenshotOcr {
        static readonly TranslationCache aiCache=new TranslationCache();
        static readonly object cacheLock=new object();
        public static string[] Languages(){return OcrEngine.AvailableRecognizerLanguages.Select(x=>x.LanguageTag).ToArray();}
        static OcrEngine CreateEngine(string language){if(!string.IsNullOrWhiteSpace(language)&&language!="auto"){var selected=OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(x=>x.LanguageTag.Equals(language,StringComparison.OrdinalIgnoreCase));var explicitEngine=selected==null?null:OcrEngine.TryCreateFromLanguage(selected);if(explicitEngine==null)throw new InvalidOperationException("截图识别语言不可用，请安装对应的 Windows OCR 语言包，或在设置中改为 auto。 / Install the selected Windows OCR language or select auto.");return explicitEngine;}var languages=OcrEngine.AvailableRecognizerLanguages;var chinese=languages.FirstOrDefault(x=>x.LanguageTag.StartsWith("zh",StringComparison.OrdinalIgnoreCase));var engine=chinese!=null?OcrEngine.TryCreateFromLanguage(chinese):OcrEngine.TryCreateFromUserProfileLanguages();if(engine==null){var english=languages.FirstOrDefault(x=>x.LanguageTag.StartsWith("en",StringComparison.OrdinalIgnoreCase));if(english!=null)engine=OcrEngine.TryCreateFromLanguage(english);}if(engine==null)throw new InvalidOperationException("Windows 尚未安装文字识别语言。请在 Windows 设置 → 时间和语言 → 语言选项中安装所需语言的 OCR 功能。 / Install a Windows OCR language feature.");return engine;}
        public static async Task<string> RecognizeAsync(BitmapSource image,CancellationToken token,string language="auto"){
            token.ThrowIfCancellationRequested();if(image==null)throw new ArgumentNullException(nameof(image));var engine=CreateEngine(language);int maximum=(int)OcrEngine.MaxImageDimension;
            BitmapSource working=image;
            if(image.PixelWidth>maximum||image.PixelHeight>maximum){double ratio=(double)maximum/Math.Max(image.PixelWidth,image.PixelHeight);int width=Math.Max(1,(int)Math.Floor(image.PixelWidth*ratio)),height=Math.Max(1,(int)Math.Floor(image.PixelHeight*ratio));var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(image,new Rect(0,0,width,height));var scaled=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);scaled.Render(visual);scaled.Freeze();working=scaled;}
            BitmapSource converted=working;if(working.Format!=PixelFormats.Bgra32){converted=new FormatConvertedBitmap(working,PixelFormats.Bgra32,null,0);converted.Freeze();}int stride=checked(converted.PixelWidth*4);var pixels=new byte[checked(stride*converted.PixelHeight)];converted.CopyPixels(pixels,stride,0);
            using(var bitmap=new SoftwareBitmap(BitmapPixelFormat.Bgra8,converted.PixelWidth,converted.PixelHeight,BitmapAlphaMode.Ignore)){
                bitmap.CopyFromBuffer(pixels.AsBuffer());var result=await engine.RecognizeAsync(bitmap).AsTask(token);token.ThrowIfCancellationRequested();return CleanText(string.Join("\n",result.Lines.Select(line=>line.Text)));
            }
        }
        public static async Task<string> RecognizeWithServiceAsync(BitmapSource image,Settings settings,CancellationToken token,System.Net.Http.HttpMessageHandler testHandler=null){
            token.ThrowIfCancellationRequested();if(image==null)throw new ArgumentNullException(nameof(image));
            if(settings.Text("provider")=="Bing")return await RecognizeAsync(image,token,settings.Text("ocr_language"));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));byte[] png;using(var memory=new System.IO.MemoryStream()){encoder.Save(memory);png=memory.ToArray();}
            token.ThrowIfCancellationRequested();string digest;using(var hash=System.Security.Cryptography.SHA256.Create())digest=Convert.ToBase64String(hash.ComputeHash(png));
            string cacheKey=Translation.CacheKey("ocr:"+digest+":"+settings.Text("ocr_model"),settings);string cached;lock(cacheLock){if(aiCache.TryGetValue(cacheKey,out cached))return cached;}
            string recognized=NormalizeAiText(await Translation.RunAsync("",settings,token,null,testHandler,png));token.ThrowIfCancellationRequested();if(!string.IsNullOrWhiteSpace(recognized))lock(cacheLock)aiCache.Add(cacheKey,recognized);return recognized;
        }
        public static string NormalizeAiText(string text){string value=CleanText(text);string plain=value.Trim(' ', '\"', '\'', '。', '.', '!', '！', ':', '：', '`');if(Regex.IsMatch(plain,@"^(?:返回空字符串|空字符串|未识别到文字|未检测到文字|没有可识别(?:的)?文字|无文字|没有文字|return an empty string|empty string|no text(?: detected| found)?|null)$",RegexOptions.IgnoreCase)||value=="\"\""||value=="''")return "";return value;}
        public static string CleanText(string text){return Regex.Replace((text??"").Replace("\r\n","\n"),@"(?<=[\u3400-\u9fff])[ \t]+(?=[\u3400-\u9fff])","").Trim();}
    }
}


