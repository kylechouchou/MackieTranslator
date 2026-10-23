using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace MackieTranslator {
 public sealed class TranslationRecord {public string Source,Result,Provider,Time;}
 public sealed class TranslationJournal {
  readonly object sync=new object();readonly List<TranslationRecord> entries=new List<TranslationRecord>();readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);readonly string path;readonly Func<byte[],byte[]> protect,unprotect;int version,savedVersion;
  public string LoadStatus {get;private set;}="未加载";
  public TranslationJournal():this(Path.Combine(Path.GetDirectoryName(Settings.FilePath),"translation-history.dat"),data=>ProtectedData.Protect(data,null,DataProtectionScope.CurrentUser),data=>ProtectedData.Unprotect(data,null,DataProtectionScope.CurrentUser)){}
  internal TranslationJournal(string file,Func<byte[],byte[]> encoder,Func<byte[],byte[]> decoder){path=file;protect=encoder;unprotect=decoder;}
  static TranslationRecord Copy(TranslationRecord x){return new TranslationRecord{Source=x.Source,Result=x.Result,Provider=x.Provider,Time=x.Time};}
  public TranslationRecord[] Entries {get{lock(sync)return entries.Select(Copy).ToArray();}}
  public void Add(string source,string result,string provider){if(string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(result)||source.Length>60000||result.Length>60000)return;lock(sync){entries.RemoveAll(x=>x.Source==source&&x.Result==result&&x.Provider==provider);entries.Insert(0,new TranslationRecord{Source=source,Result=result,Provider=provider,Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm")});if(entries.Count>20)entries.RemoveRange(20,entries.Count-20);version++;}}
  public void Clear(){lock(sync){entries.Clear();version++;}}
  public void Load(){try{if(!File.Exists(path)){LoadStatus="无历史文件";return;}if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException();string json=Encoding.UTF8.GetString(unprotect(File.ReadAllBytes(path)));var list=new JavaScriptSerializer{MaxJsonLength=16000000}.DeserializeObject(json) as object[];if(list==null)throw new InvalidDataException();var validated=new List<TranslationRecord>();foreach(var raw in list.Take(20)){var map=raw as Dictionary<string,object>;if(map==null)throw new InvalidDataException();object a,b,c,d;if(!map.TryGetValue("Source",out a)||!map.TryGetValue("Result",out b)||!map.TryGetValue("Provider",out c)||!map.TryGetValue("Time",out d))throw new InvalidDataException();string source=a as string,result=b as string;if(string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(result)||source.Length>60000||result.Length>60000)throw new InvalidDataException();validated.Add(new TranslationRecord{Source=source,Result=result,Provider=c as string??"",Time=d as string??""});}lock(sync){entries.Clear();entries.AddRange(validated);version=savedVersion=0;}LoadStatus="已载入";LocalDiagnostics.Record(CaptureStep.HistoryLoaded);}catch(Exception error){LoadStatus="历史读取失败，原文件已保留";LocalDiagnostics.Record(CaptureStep.HistoryError,error.HResult);}}
  public async Task SaveAsync(){int requested;lock(sync){requested=version;if(requested==savedVersion)return;}await gate.WaitAsync().ConfigureAwait(false);try{TranslationRecord[] snapshot;lock(sync){if(requested!=version||requested==savedVersion)return;snapshot=entries.Select(Copy).ToArray();}await Task.Run(()=>{byte[] bytes=protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=16000000}.Serialize(snapshot)));if(bytes.Length>16*1024*1024)throw new InvalidDataException();string folder=Path.GetDirectoryName(path);Directory.CreateDirectory(folder);string temp=path+".tmp";try{File.WriteAllBytes(temp,bytes);lock(sync){if(requested!=version)return;if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);savedVersion=requested;}LocalDiagnostics.Record(CaptureStep.HistorySaved);}finally{if(File.Exists(temp))File.Delete(temp);}}).ConfigureAwait(false);}catch(Exception error){LocalDiagnostics.Record(CaptureStep.HistoryError,error.HResult);throw;}finally{gate.Release();}}
 }
}
