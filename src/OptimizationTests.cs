using System;
using System.IO;
using System.Text.RegularExpressions;
namespace MackieTranslator {
    public static class OptimizationTests {
        static int count;
        static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
        public static int Run(){try {
            var random=new Random(132);string alphabet="abAB19-’' _.,\n";
            for(int sample=0;sample<300;sample++){var chars=new char[random.Next(1,400)];for(int i=0;i<chars.Length;i++)chars[i]=alphabet[random.Next(alphabet.Length)];string text=new string(chars);int expected=Regex.Matches(text,@"[A-Za-z]+(?:[-'’][A-Za-z]+)*").Count;Check(TextTools.Counts(text,true)=="0 chars · "+expected+" words","Word counting retains existing semantics");}
            Check(TextTools.Counts("中文\U00020000",false)=="3字·0词","Supplementary Chinese count preserved");
            Check(TextTools.Prepare("part-\nword",true,false)=="part-word","Join rules unchanged");
            var history=new History();for(int i=0;i<40;i++)history.Push(new string('a',60000)+i,new string('b',60000)+i);Check(history.RetainedCharacters<=512*1024&&history.Count>0,"History bounded by total characters");string undone=history.Undo("latest");Check(undone.EndsWith("39"),"Most recent large edit undo retained");Check(history.Redo(undone)=="latest","Redo retained");history.Push(new string('a',600000),"small");Check(history.RetainedCharacters<=512*1024,"Oversized snapshots do not enter history");
            var cache=new TranslationCache();for(int i=0;i<40;i++)cache.Add("key"+i,new string('x',40000));Check(cache.RetainedCharacters<=256*1024,"Translation cache bounded by total characters");string value;Check(cache.TryGetValue("key39",out value)&&value.Length==40000,"Latest cache entry retained");Check(!cache.TryGetValue("key0",out value),"Old cache entry evicted");int before=cache.RetainedCharacters;cache.Add("key39","duplicate");Check(cache.RetainedCharacters==before,"Duplicate cache insertion does not corrupt eviction queue");cache.Add("huge",new string('z',300000));Check(cache.RetainedCharacters==before,"Large result bypasses cache");cache.Clear();Check(cache.RetainedCharacters==0,"Cache clear releases entries");
            Check(ScreenshotSelection.OcrSize(new System.Drawing.Size(6000,3000),2600)==new System.Drawing.Size(2600,1300),"Large screenshot resized before managed pixel copy");Check(ScreenshotSelection.OcrSize(new System.Drawing.Size(1000,500),2600)==new System.Drawing.Size(1000,500),"Small screenshot resolution preserved");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"optimization-test-result.txt"),count+" optimization checks passed.\n");return 0;
        }catch(Exception error){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"optimization-test-result.txt"),"FAILED: "+error);return 1;}}
    }
}
