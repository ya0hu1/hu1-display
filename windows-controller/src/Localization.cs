using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Markup;

namespace DeskMateLearning
{
    public sealed class LanguagePreference { public string Language; }
    public sealed class LocalizedEntry { public string Key,Zh,En; }
    public sealed class LocalizationCatalog { public LocalizedEntry[] Entries; }

    // A single explicit UI language; never changes protocol encoding or device identities.
    public static class UiText
    {
        private static readonly LocalizedEntry[] entries;
        private static readonly Dictionary<string,LocalizedEntry> bySource=new Dictionary<string,LocalizedEntry>(StringComparer.Ordinal);
        private static readonly Dictionary<string,LocalizedEntry> byEnglish=new Dictionary<string,LocalizedEntry>(StringComparer.Ordinal);
        private static string language=SystemLanguage;
        static UiText()
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Huiying.UiStrings.json")){
                if(stream==null)throw new InvalidDataException("UI language catalog is missing.");
                using(var reader=new StreamReader(stream)){
                    var catalog=new JavaScriptSerializer().Deserialize<LocalizationCatalog>(reader.ReadToEnd());
                    if(catalog==null || catalog.Entries==null)throw new InvalidDataException("Invalid UI language catalog.");
                    entries=catalog.Entries;
                }
            }
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in entries){
                if(String.IsNullOrWhiteSpace(entry.Key) || entry.Zh==null || entry.En==null || !keys.Add(entry.Key) || bySource.ContainsKey(entry.Zh))
                    throw new InvalidDataException("Duplicate or invalid UI language entry.");
                bySource.Add(entry.Zh,entry);
                if(!byEnglish.ContainsKey(entry.En))byEnglish.Add(entry.En,entry);
            }
        }
        public static string SystemLanguage {get{return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="zh" ? "zh-CN" : "en-US";}}
        public static string Language {get{return language;}}
        public static bool English {get{return language=="en-US";}}
        public static string Brand {get{return English ? "hu1 display" : "晖映";}}
        public static string ReadLanguage(string directory)
        {
            try{
                string file=Path.Combine(directory,"studio-language.json");
                if(!File.Exists(file))return SystemLanguage;
                var saved=new JavaScriptSerializer().Deserialize<LanguagePreference>(File.ReadAllText(file));
                return saved!=null && (saved.Language=="zh-CN" || saved.Language=="en-US") ? saved.Language : SystemLanguage;
            }catch(IOException){return SystemLanguage;}catch(UnauthorizedAccessException){return SystemLanguage;}
            catch(ArgumentException){return SystemLanguage;}catch(InvalidOperationException){return SystemLanguage;}
        }
        public static void ValidateLanguage(string value)
        {if(value!="zh-CN" && value!="en-US")throw new ArgumentException("Supported UI languages are zh-CN and en-US.","value");}
        public static void SetLanguage(string value){ValidateLanguage(value);language=value;}
        public static void SaveLanguage(string directory,string value)
        {ValidateLanguage(value);StudioServices.AtomicJson(Path.Combine(directory,"studio-language.json"),new LanguagePreference{Language=value});}
        public static string Get(string source)
        {
            if(source==null)return "";
            LocalizedEntry entry;
            if(bySource.TryGetValue(source,out entry) || byEnglish.TryGetValue(source,out entry))return English ? entry.En : entry.Zh;
            return source; // Native OS/third-party diagnostic text is preserved, never hidden.
        }
        public static string Format(string source,params object[] values)
        {return String.Format(English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("zh-CN"),Get(source),values);}
        public static void Apply(Window window)
        {
            foreach(var entry in entries)window.Resources[entry.Key]=English ? entry.En : entry.Zh;
            window.Language=XmlLanguage.GetLanguage(language);
            window.FontFamily=new System.Windows.Media.FontFamily(English ? "Segoe UI" : "Microsoft YaHei UI");
        }
    }
}
