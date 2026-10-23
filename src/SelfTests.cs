using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
namespace MackieTranslator {
    public static class SelfTests {
        sealed class Mock:HttpMessageHandler {
            readonly string response;readonly Action<HttpRequestMessage,string> inspect;
            public Mock(string data,Action<HttpRequestMessage,string> check=null){response=data;inspect=check;}
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){inspect?.Invoke(request,await request.Content.ReadAsStringAsync());return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")};}
        }
        static int count;
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;}
        public static int Run(){try{
            var s=new Settings();Check(s.Text("provider")=="Bing","Default provider");Check(s.Text("mode")=="即时划词","Default capture");Check(s.Number("font_size",0)==11,"Default 11-point text");s.Values["font_size"]=double.NaN;Check(s.Number("font_size",12)==12,"Reject non-finite font size");
            Check(TextTools.Prepare("Hello\nworld")=="Hello world","English line cleanup");
            Check(TextTools.Prepare("你好\n世界")=="你好世界","Chinese line cleanup");
            Check(TextTools.Prepare("para\n\nnext")=="para\n\nnext","Paragraph preservation");
            Check(TextTools.Prepare("ther-\nmal",true,true)=="thermal","Split-word repair");
            Check(TextTools.Prepare("text\n- item")=="text\n- item","List preservation");
            Check(TextTools.Prepare("ﬁne")=="fine","Ligature normalization");
            Check(TextTools.DetectDirection("A shell-and-tube thermal battery.")=="外译中","Automatic English direction");Check(TextTools.DetectDirection("硫化物热电池的温度分布。") =="中译英","Automatic Chinese direction");Check(TextTools.DetectDirection("温度分布采用 CFD 模型分析。") =="中译英","Chinese text with English terminology");Check(TextTools.DetectDirection("The temperature was measured using the 硫 battery.")=="外译中","English text with a Chinese term");Check(TextTools.DetectDirection("中")=="中译英","Single Chinese character");
            Check(TextTools.LooksSensitive("stored-test-key","stored-test-key"),"Detect own key in capture");Check(TextTools.LooksSensitive("sk-0123456789abcdefghijklmnop",""),"Detect API key capture");Check(TextTools.LooksSensitive("-----BEGIN RSA PRIVATE KEY-----\nexample",""),"Detect private-key capture");Check(!TextTools.LooksSensitive("A normal research paragraph.","stored-test-key"),"Allow ordinary translation text");
            Check(TextTools.Prepare("```\ncode\n``` ")=="```\ncode\n```","Code preservation");
            Check(TextTools.Counts("你好 shell-and-tube",false)=="2字·1词","Word counts");
            var h=new History();for(int i=0;i<25;i++)h.Push(i.ToString(),(i+1).ToString());Check(h.Count==20,"20-step bound");
            Check(h.Undo("25")=="24","Undo");Check(h.Redo("24")=="25","Redo");h.Reset();Check(h.Count==0,"Capture resets history");
            s.Values["provider"]="DeepSeek";Check(Translation.Endpoint(s).ToString()=="https://api.deepseek.com/chat/completions","DeepSeek endpoint");
            s.Values["base_url"]="https://attacker.example";bool invalid=false;try{Translation.Endpoint(s);}catch{invalid=true;}Check(invalid,"Reject external API host");
            byte[] plain=Encoding.UTF8.GetBytes("test-key");byte[] cipher=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);Check(!cipher.SequenceEqual(plain)&&ProtectedData.Unprotect(cipher,null,DataProtectionScope.CurrentUser).SequenceEqual(plain),"DPAPI roundtrip");
            var fonts=Native.Fonts();Check(fonts.Length>0&&fonts.All(x=>!x.StartsWith("@")),"Local font enumeration");Check(!Native.ValidFont("@Arial"),"Reject vertical font");
            string dir=Path.Combine(Path.GetTempPath(),"MackieTranslator-selftest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string old=Environment.GetEnvironmentVariable("LOCALAPPDATA");Environment.SetEnvironmentVariable("LOCALAPPDATA",dir);
            try{var config=new Settings{Key="self-test-secret"};config.SaveAsync().GetAwaiter().GetResult();string disk=File.ReadAllText(Settings.FilePath);Check(!disk.Contains(config.Key),"No plaintext key on disk");var loaded=new Settings();loaded.Load();Check(loaded.Key==config.Key&&loaded.Text("provider")=="Bing","Settings roundtrip");var rules=File.GetAccessControl(Settings.FilePath).GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier));Check(File.GetAccessControl(Settings.FilePath).AreAccessRulesProtected,"Protected settings ACL");Check(!rules.Cast<System.Security.AccessControl.FileSystemAccessRule>().Any(r=>r.IdentityReference.Value=="S-1-1-0"||r.IdentityReference.Value=="S-1-5-32-545"),"No Everyone or Users settings permission");Environment.SetEnvironmentVariable("LOCALAPPDATA",@"\\example.invalid\share");bool rejected=false;try{var path=Settings.FilePath;}catch{rejected=true;}Check(rejected,"Reject network configuration path");Environment.SetEnvironmentVariable("LOCALAPPDATA",dir);}
            finally{Environment.SetEnvironmentVariable("LOCALAPPDATA",old);foreach(string f in Directory.GetFiles(dir,"*",SearchOption.AllDirectories))File.Delete(f);Directory.Delete(Path.Combine(dir,"SelectionTranslate"));Directory.Delete(dir);}
            var deep=new Settings {Key="test-api-key"};deep.Values["provider"]="DeepSeek";deep.Values["direction"]="中译英";
            using(var handler=Translation.CreateHandler())Check(!handler.AllowAutoRedirect&&!handler.UseDefaultCredentials,"Network redirects and implicit authentication disabled");
            var badKey=new Settings {Key="key\tinjection"};badKey.Values["provider"]="DeepSeek";bool badKeyRejected=false;try{Translation.RunAsync("test",badKey,CancellationToken.None,null,new Mock("{}" )).GetAwaiter().GetResult();}catch{badKeyRejected=true;}Check(badKeyRejected,"Reject control characters in API key");
            bool oversized=false;try{Translation.RunAsync(new string('a',60001),new Settings(),CancellationToken.None,null,new Mock("{}")).GetAwaiter().GetResult();}catch{oversized=true;}Check(oversized,"Request text size limit");
            bool responseLimit=false;try{Translation.RunAsync("test",new Settings(),CancellationToken.None,null,new Mock(new string('x',4*1024*1024+1))).GetAwaiter().GetResult();}catch(InvalidOperationException error){responseLimit=error.Message.Contains("size limit");}Check(responseLimit,"Response size limit");
            var mock=new Mock("{\"choices\":[{\"message\":{\"content\":\"Hello\"},\"finish_reason\":\"stop\"}]}",(request,body)=>{Check(request.Headers.Authorization.Parameter=="test-api-key","DeepSeek authorization");Check(body.Contains("将中文翻译"),"Reverse translation prompt");});
            Check(Translation.RunAsync("你好",deep,CancellationToken.None,null,mock).GetAwaiter().GetResult()=="Hello","DeepSeek response");
            var streamMock=new Mock("data: {\"choices\":[{\"delta\":{\"content\":\"中文\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");string last="";
            Check(Translation.RunAsync("Hello",deep,CancellationToken.None,v=>last=v,streamMock).GetAwaiter().GetResult()=="中文"&&last=="中文","Unicode streaming");
            bool incomplete=false;try{Translation.RunAsync("Hello",deep,CancellationToken.None,v=>{},new Mock("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n")).GetAwaiter().GetResult();}catch{incomplete=true;}Check(incomplete,"Reject incomplete streaming");
            var bing=new Settings {Key="must-not-send"};Check(Translation.RunAsync("Hello",bing,CancellationToken.None,null,new Mock("[{\"translations\":[{\"text\":\"你好\"}]}]",(request,body)=>{Check(request.RequestUri.Host=="edge.microsoft.com"&&!request.Headers.Contains("Authorization")&&!body.Contains("must-not-send"),"No API secret in Bing request");})).GetAwaiter().GetResult()=="你好","Bing response");
            Check(Translation.RunAsync("你好",bing,CancellationToken.None,null,new Mock("[{\"translations\":[{\"text\":\"Hello\"}]}]",(request,body)=>Check(request.RequestUri.Query.Contains("from=zh-Hans")&&request.RequestUri.Query.Contains("to=en"),"Bing automatic Chinese request"))).GetAwaiter().GetResult()=="Hello","Bing automatic reverse response");
            string translated=Translation.RunAsync("Hello, world.",bing,CancellationToken.None).GetAwaiter().GetResult();Check(System.Text.RegularExpressions.Regex.IsMatch(translated,@"[\u4e00-\u9fff]"),"Live Bing translation");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-result.txt"),count+" checks passed.\n",Encoding.UTF8);return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-result.txt"),"FAILED: "+error.ToString(),Encoding.UTF8);return 1;}}
    }
}
