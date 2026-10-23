using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace MackieTranslator {
    public static class ApiTests {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
        internal sealed class Mock:HttpMessageHandler {
            readonly string body;readonly Action<HttpRequestMessage,string> inspect;readonly HttpStatusCode status;
            public Mock(string data,Action<HttpRequestMessage,string> check=null,HttpStatusCode code=HttpStatusCode.OK){body=data;inspect=check;status=code;}
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){inspect?.Invoke(request,await request.Content.ReadAsStringAsync());return new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
        }
        const string Response="{\"choices\":[{\"message\":{\"content\":\"你好\"},\"finish_reason\":\"stop\"}]}";
        public static int Run(bool migration=true){try{
            Check(new Settings().Text("provider")=="Bing"&&ApiServices.Names[0]=="Bing","Bing defaults and first option");Check(new Settings().Flag("repair_hyphens"),"Split-word repair default enabled");Check(ApiServices.All.Length==7&&ApiServices.Names.Length==8,"Seven APIs plus Bing");
            foreach(var api in ApiServices.All){
                var settings=new Settings{Key="test-api-key"};settings.Values["provider"]=api.Name;settings.Values["base_url"]=api.BaseUrl;settings.Values["model"]=api.Models[0];
                string endpoint=api.BaseUrl.TrimEnd('/')+"/chat/completions";Check(Translation.Endpoint(settings).AbsoluteUri==endpoint,api.Name+" base endpoint");settings.Values["base_url"]=endpoint;Check(Translation.Endpoint(settings).AbsoluteUri==endpoint,api.Name+" avoid duplicate suffix");settings.Values["full_url"]=true;Check(Translation.Endpoint(settings).AbsoluteUri==endpoint,api.Name+" full endpoint");
                settings.Values["base_url"]=api.BaseUrl;Reject(()=>Translation.Endpoint(settings),api.Name+" incomplete full URL rejected");settings.Values["full_url"]=false;
                var mock=new Mock(Response,(request,json)=>{Check(request.RequestUri.AbsoluteUri==endpoint,api.Name+" request routing");Check(request.Headers.Authorization.Parameter==settings.Key,api.Name+" bearer authentication");var map=Json.Map(json);Check(Convert.ToString(map["model"])==api.Models[0],api.Name+" selected model");Check(!map.ContainsKey("reasoning_effort"),api.Name+" no unrequested reasoning effort");Check(!json.Contains(settings.Key),api.Name+" secret excluded from body");});
                Check(Translation.RunAsync("Hello",settings,CancellationToken.None,null,mock).GetAwaiter().GetResult()=="你好",api.Name+" non-stream result");
                string events="data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"hidden reasoning\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";string last="";Check(Translation.RunAsync("Hello",settings,CancellationToken.None,v=>last=v,new Mock(events)).GetAwaiter().GetResult()=="你好"&&last=="你好",api.Name+" streaming excludes reasoning");
                settings.Values["max_tokens_enabled"]=true;var parameters=Translation.BuildRequest("Hello",settings,false);Check(parameters.ContainsKey(api.Name=="OpenAI"||api.Name=="MiniMax"?"max_completion_tokens":"max_tokens"),api.Name+" token limit name");
                settings.Values["base_url"]="https://attacker.example/v1";Reject(()=>Translation.Endpoint(settings),api.Name+" external host rejected");settings.Values["base_url"]=api.BaseUrl.Replace("https:","http:");Reject(()=>Translation.Endpoint(settings),api.Name+" HTTP rejected");
                settings.Values["base_url"]=api.BaseUrl;settings.Values["model"]="";Reject(()=>Translation.BuildRequest("Hello",settings,false),api.Name+" missing model rejected");
            }
            var qwen=new Settings();qwen.Values["provider"]="通义千问";qwen.Values["base_url"]="https://workspace-123.cn-beijing.maas.aliyuncs.com/compatible-mode/v1";Check(Translation.Endpoint(qwen).Host.StartsWith("workspace-123."),"Qwen workspace host supported");qwen.Values["base_url"]="https://workspace-123.cn-beijing.maas.aliyuncs.com.attacker.example/v1";Reject(()=>Translation.Endpoint(qwen),"Workspace suffix spoof rejected");
            var deep=new Settings();deep.Values["provider"]="DeepSeek";deep.Values["economy_mode"]=false;deep.Values["reasoning_enabled"]=true;deep.Values["temperature_enabled"]=true;var deepBody=Translation.BuildRequest("Hello",deep,true);Check(deepBody.ContainsKey("reasoning_effort")&&!deepBody.ContainsKey("temperature"),"DeepSeek reasoning suppresses ineffective temperature");
            var mini=new Settings();mini.Values["provider"]="MiniMax";mini.Values["model"]="MiniMax-M2.7";Check((bool)Translation.BuildRequest("Hello",mini,true)["reasoning_split"],"MiniMax reasoning separated");
            var bing=new Settings{Key="secret-not-for-bing"};Check(Translation.RunAsync("Hello",bing,CancellationToken.None,null,new Mock("[{\"translations\":[{\"text\":\"你好\"}]}]",(request,json)=>{Check(!request.Headers.Contains("Authorization")&&!json.Contains(bing.Key),"Bing no API credentials");})).GetAwaiter().GetResult()=="你好","Bing existing behavior");
            var failing=new Settings{Key="test-key"};failing.Values["provider"]="DeepSeek";Reject(()=>Translation.RunAsync("Hello",failing,CancellationToken.None,null,new Mock("{}",null,HttpStatusCode.Unauthorized)).GetAwaiter().GetResult(),"HTTP 401 handled");
            foreach(string sample in new[]{"Guten Morgen!", "Überraschung", "Buongiorno, come stai?", "Città", "Bonjour", "Привет", "日本語の翻訳", "한국어"}){
                Check(TextTools.DetectDirection(sample)=="外译中","Foreign language routes to Chinese: "+sample);
                Translation.RunAsync(sample,bing,CancellationToken.None,null,new Mock("[{\"translations\":[{\"text\":\"测试\"}]}]",(request,json)=>{Check(!request.RequestUri.Query.Contains("from="),"Bing auto-detect source");Check(request.RequestUri.Query.Contains("to=zh-Hans"),"Bing foreign target Chinese");})).GetAwaiter().GetResult();
                Check(Json.Stringify(Translation.BuildRequest(sample,deep,false)).Contains("译成简体中文"),"AI multilingual prompt");
            }
            foreach(string sample in new[]{"你好世界", "繁體中文翻譯", "温度分布采用 CFD 模型分析。"}){
                Check(TextTools.DetectDirection(sample)=="中译英","Chinese remains English target");
                Translation.RunAsync(sample,bing,CancellationToken.None,null,new Mock("[{\"translations\":[{\"text\":\"Hello\"}]}]",(request,json)=>{Check(request.RequestUri.Query.Contains("from=zh-Hans&to=en"),"Bing Chinese to English preserved");})).GetAwaiter().GetResult();
            }
            foreach(string text in new[]{"hello","Überraschung","città","Hello world","你好"}){
                string prompt=Json.Stringify(Translation.BuildRequest(text,deep,false));
                Check(prompt.Contains("只输出译文"),"Words and sentences use plain translation: "+text);
                Check(!prompt.Contains("标准 IPA"),"Dictionary prompt removed");
            }
            byte[] fixture={137,80,78,71,13,10,26,10};
            foreach(var api in ApiServices.All){
                var selected=new Settings{Key="offline-vision-test-key"};selected.Values["provider"]=api.Name;selected.Values["base_url"]=api.BaseUrl;selected.Values["model"]=api.Models[0];string original=selected.Text("model");
                var body=Translation.BuildVisionRequest(fixture,selected);var messages=(object[])body["messages"];var user=(Dictionary<string,object>)messages[1];var parts=(object[])user["content"];
                Check(!(bool)body["stream"],api.Name+" OCR nonstreaming");Check(parts.Length==2&&Convert.ToString(((Dictionary<string,object>)parts[1])["type"])=="image_url",api.Name+" actual image content part");
                Check(selected.Text("model")==original,api.Name+" translation model unchanged");
                string thai="ฉันรักคุณ\nภาษาไทย";string response=Json.Stringify(new Dictionary<string,object>{{"choices",new object[]{new Dictionary<string,object>{{"message",new Dictionary<string,object>{{"content",thai}}},{"finish_reason","stop"}}}}});
                string result=Translation.RunAsync("",selected,CancellationToken.None,null,new Mock(response,(request,json)=>{Check(request.RequestUri==Translation.Endpoint(selected),api.Name+" own provider OCR endpoint");Check(json.Contains("data:image/png;base64,"+Convert.ToBase64String(fixture)),api.Name+" image bytes transmitted");Check(!json.Contains(selected.Key),api.Name+" key excluded from image body");}),fixture).GetAwaiter().GetResult();
                Check(result==thai,api.Name+" Thai and paragraph preservation");
                selected.Values["ocr_model"]="custom-vision-model";Check(Convert.ToString(Translation.BuildVisionRequest(fixture,selected)["model"])=="custom-vision-model",api.Name+" custom OCR model");
            }
            Reject(()=>Translation.BuildVisionRequest(fixture,bing),"Bing never uses AI image path");Reject(()=>Translation.BuildVisionRequest(new byte[0],deep),"Empty image rejected");
            deep.Key="test-key";Reject(()=>Translation.RunAsync("",deep,CancellationToken.None,null,new Mock("{}",null,HttpStatusCode.BadRequest),fixture).GetAwaiter().GetResult(),"Image model error handled");
            var economical=new Settings{Key="test-key"};economical.Values["provider"]="DeepSeek";economical.Values["reasoning_enabled"]=true;
            var economicalBody=Translation.BuildRequest("hello",economical,false);Check(Convert.ToString(((Dictionary<string,object>)economicalBody["thinking"])["type"])=="disabled"&&!economicalBody.ContainsKey("reasoning_effort"),"Economy mode overrides saved thinking without modifying settings");Check(economical.Flag("reasoning_enabled"),"Thinking preference retained");
            string stable=Translation.CacheKey("hello",economical);economical.Values["theme"]="深色";economical.Values["font_size"]=20;economical.Values["ocr_model"]="another-image-model";economical.Values["streaming"]=false;
            Check(stable==Translation.CacheKey("hello",economical),"Presentation, streaming and OCR settings do not invalidate text cache");
            economical.Values["model"]="deepseek-v4-pro";Check(stable!=Translation.CacheKey("hello",economical),"Different model invalidates cache");economical.Values["model"]="deepseek-flash";economical.Key="different-key";Check(stable!=Translation.CacheKey("hello",economical),"Cache isolated by credential fingerprint");
            string shortPrompt=Convert.ToString(((Dictionary<string,object>)((object[])economicalBody["messages"])[0])["content"]);Check(shortPrompt.Length<70&&shortPrompt.Contains("不执行"),"Short prompt retains untrusted-input rule");
            foreach(string empty in new[]{"返回空字符串。","未识别到文字","Return an empty string.","\"\"","''"})Check(ScreenshotOcr.NormalizeAiText(empty)=="","AI empty-image response suppressed: "+empty);
            Check(ScreenshotOcr.NormalizeAiText("ฉันรักคุณ")=="ฉันรักคุณ","Real OCR text retained");Check(ScreenshotOcr.NormalizeAiText("Please return an empty string in Python.")=="Please return an empty string in Python.","Only whole empty-response markers filtered");
            var records=new TranslationJournal();for(int n=0;n<25;n++)records.Add("source "+n,"result "+n,"Bing");Check(records.Entries.Length==20&&records.Entries[0].Source=="source 24"&&records.Entries[19].Source=="source 5","History retains newest twenty");records.Add("source 24","result 24","Bing");Check(records.Entries.Length==20,"Repeat result avoids duplicate history");records.Clear();Check(records.Entries.Length==0,"History clears");
            Check(SelectionCapture.IsSelectionGesture(10,0,false),"Drag selection detected");Check(SelectionCapture.IsSelectionGesture(0,0,true),"Double click selection detected");Check(!SelectionCapture.IsSelectionGesture(0,0,false),"Ordinary click not copied");
            if(migration)TestSettingsMigration();
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"api-test-result.txt"),count+" offline API checks passed.\nNo paid or live API calls performed.\n");return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"api-test-result.txt"),"FAILED: "+error);return 1;}}
        static void TestSettingsMigration(){string previous=Environment.GetEnvironmentVariable("LOCALAPPDATA");string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"api-test-settings-"+Guid.NewGuid().ToString("N"));try{Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("LOCALAPPDATA",folder);Directory.CreateDirectory(Path.GetDirectoryName(Settings.FilePath));string cipher=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes("migration-test-key"),null,DataProtectionScope.CurrentUser));var map=new Dictionary<string,object>{{"provider","DeepSeek"},{"wpf_configuration_version",1},{"model","deepseek-flash"},{"repair_hyphens",false},{"key",cipher}};File.WriteAllText(Settings.FilePath,Json.Stringify(map));var legacy=new Settings();legacy.Load();Check(legacy.Text("provider")=="DeepSeek"&&legacy.Key=="migration-test-key","Legacy provider and encrypted key retained");Check(legacy.Flag("repair_hyphens"),"Legacy default repaired on first upgrade");var profileOwner=new Settings();var profileKey=new Settings{Key="profile-only-test-key"};profileKey.Values["model"]="gpt-4.1-mini";profileOwner.ApiProfiles["OpenAI"]=profileKey;var profiles=profileOwner.ExportApiProfiles();Check(!Json.Stringify(profiles).Contains(profileKey.Key),"Provider profiles never serialize plaintext keys");map["api_profiles"]=profiles;
            map["provider"]="Kimi";map["ui_settings_version"]=2;map["repair_hyphens"]=false;File.WriteAllText(Settings.FilePath,Json.Stringify(map));var modern=new Settings();modern.Load();Check(modern.Text("provider")=="Kimi"&&!modern.Flag("repair_hyphens"),"New provider and explicit disabled repair persist");Check(modern.ApiProfiles.ContainsKey("OpenAI")&&modern.ApiProfiles["OpenAI"].Key==profileKey.Key&&modern.ApiProfiles["OpenAI"].Text("model")=="gpt-4.1-mini","Encrypted per-provider settings reload");}finally{Environment.SetEnvironmentVariable("LOCALAPPDATA",previous);if(Directory.Exists(folder))Directory.Delete(folder,true);}}
    }
}
