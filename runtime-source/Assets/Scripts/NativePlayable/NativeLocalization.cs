using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Fumo.NativePlayable
{
    public static class NativeLocalization
    {
        [Serializable] sealed class Phrase { public string key,english,spanish,custom; }
        [Serializable] sealed class Catalog { public Phrase[] phrases; }
        [Serializable] sealed class Translation { public string key,value; public bool preserveFirst; }
        [Serializable] sealed class ChineseCatalog { public Translation[] phrases,texts,prefixes,patterns; }
        static readonly Dictionary<string,string> chinesePhrases=new Dictionary<string,string>(StringComparer.Ordinal);
        static readonly Dictionary<string,string> chineseText=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        static Translation[] chinesePrefixes=new Translation[0],chinesePatterns=new Translation[0];
        public const int ChineseLanguage=3;
        public static readonly string[] LanguageNames={"English","Español","Custom","简体中文"};
        static readonly Dictionary<string,Phrase> phrases=new Dictionary<string,Phrase>(StringComparer.Ordinal);
        static Dictionary<string,string> custom=new Dictionary<string,string>(StringComparer.Ordinal);
        static readonly Dictionary<string,string> englishKeys=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string,string> nativeSpanish=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"Navigate","Navegar"},{"Horizontal","Dirección"},{"Vertical","Vertical"},{"Settings","Opciones"},{"Race setup","Configuración de carrera"},{"Custom race","Carrera personalizada"},{"Time Trials","Contrarreloj"},{"Grand Prix","Gran Premio"},{"Records","Récords"},{"Main menu","Menú principal"},{"Resume race","Continuar carrera"},{"Restart race","Reiniciar carrera"},{"Select track","Elegir pista"},{"Select character","Elegir personaje"},{"Select vehicle","Elegir vehículo"},{"Retry","Reintentar"},{"Next station","Siguiente etapa"},{"Next race","Siguiente carrera"},{"Continue","Continuar"},{"Quit","Salir"},{"Paused","Pausa"},{"Results","Resultados"},{"Confirm","Confirmar"},{"Cancel","Cancelar"},{"Default","Predeterminado"},{"On","Activado"},{"Off","Desactivado"},{"None","Ninguno"},{"Ready","Listo"},{"Racing...","En carrera..."},{"Select mode","Elegir modo"},{"Race!","¡Carrera!"},{"Quitting to Desktop...","Saliendo al escritorio..."},{"Your best","Tu récord"},{"New best","Nuevo récord"},{"No record","Sin récord"},{"Cup ceremony","Ceremonia del Gran Premio"},{"Finish","Finalizar"},{"Return to main menu","Volver al menú principal"}
        };
        static readonly Dictionary<string,string> nativePrefixes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"Laps: ","Vueltas: "},{"Racers: ","Participantes: "},{"Items: ","Objetos: "},{"Best: ","Récord: "},{"Track: ","Pista: "},{"Mode: ","Modo: "},{"Character: ","Personaje: "},{"Vehicle: ","Vehículo: "},{"Points: ","Puntos: "},{"Rank: ","Posición: "},{"Station ","Etapa "},{"CUP STATION ","ETAPA DEL GRAN PREMIO "},{"Lap ","Vuelta "},{"Cup total: ","Total del Gran Premio: "},{"Winner: ","Ganador: "},{"Cup winner: ","Ganador del Gran Premio: "}
        };
        static bool loaded;
        public static string LastError { get; private set; }="";
        public static int SystemDefaultLanguage
        {
            get
            {
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.ChineseTraditional:
                        return ChineseLanguage;
                    case SystemLanguage.Spanish:
                        return 1;
                    default:
                        return 0;
                }
            }
        }
        public static int Language => NativeUserSettings.GetInt("lang_game");
        public static string LanguageName => LanguageNames[Language];
        public static bool IsChinese => Language==ChineseLanguage;
        public static int PhraseCount { get { Ensure(); return phrases.Count; } }
        public static string Get(string key,string fallback=null)
        {
            Ensure(); string value;
            if(IsChinese && chinesePhrases.TryGetValue(key,out value))return value;
            if(Language==2 && custom.TryGetValue(key,out value) && !string.IsNullOrEmpty(value)) return value;
            Phrase phrase;
            if(phrases.TryGetValue(key,out phrase))
            {
                value=Language==1?phrase.spanish:Language==2?phrase.custom:phrase.english;
                if(!string.IsNullOrEmpty(value)) return value;
                if(!string.IsNullOrEmpty(phrase.english)) return phrase.english;
            }
            return IsChinese?TranslateChinese(fallback??key):fallback??key;
        }
        public static string AuthoredText(string text)
        {
            if(string.IsNullOrEmpty(text)) return text;
            Ensure();
            return phrases.ContainsKey(text) ? Get(text) : IsChinese?TranslateChinese(text):text;
        }
        public static string TranslateText(string english)
        {
            if(string.IsNullOrEmpty(english))return english;Ensure();string key;
            if(phrases.ContainsKey(english))return Get(english);
            if(Language==0)return english;
            if(englishKeys.TryGetValue(english,out key))return Get(key,english);
            if(english.IndexOf('\n')>=0){var lines=english.Split('\n');for(int i=0;i<lines.Length;i++)lines[i]=TranslateText(lines[i]);return string.Join("\n",lines);}
            if(IsChinese)return TranslateChinese(english);
            if(Language==1)
            {
                string value;if(nativeSpanish.TryGetValue(english,out value))return value;
                foreach(var prefix in nativePrefixes)if(english.StartsWith(prefix.Key,StringComparison.OrdinalIgnoreCase))return prefix.Value+TranslateText(english.Substring(prefix.Key.Length));
            }
            return english;
        }
        static string TranslateChinese(string text)
        {
            if(string.IsNullOrEmpty(text))return text;
            string value;if(chineseText.TryGetValue(text,out value))return value;
            if(text.IndexOf('<')>=0)return Regex.Replace(text,@"(^|>)([^<>]+)(?=<|$)",match=>match.Groups[1].Value+TranslateText(match.Groups[2].Value));
            foreach(var pattern in chinesePatterns)
            {
                var match=Regex.Match(text,pattern.key,RegexOptions.CultureInvariant);
                if(!match.Success)continue;
                var args=new object[match.Groups.Count-1];
                for(int i=0;i<args.Length;i++)args[i]=i==0 && pattern.preserveFirst?match.Groups[i+1].Value:TranslateText(match.Groups[i+1].Value);
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,pattern.value,args);
            }
            int separator=text.IndexOf("  /  ",StringComparison.Ordinal);
            if(separator>=0)return TranslateText(text.Substring(0,separator))+"  /  "+TranslateText(text.Substring(separator+5));
            foreach(var prefix in chinesePrefixes)if(text.StartsWith(prefix.key,StringComparison.OrdinalIgnoreCase))return prefix.value+TranslateText(text.Substring(prefix.key.Length));
            return text;
        }
        static void Ensure()
        {
            if(loaded) return; loaded=true;
            var asset=NativeContent.Load<TextAsset>("NativeSettings/translations");
            if(asset==null) { LastError="Missing original translations resource"; return; }
            try { var catalog=JsonUtility.FromJson<Catalog>(asset.text); foreach(var phrase in catalog.phrases) {phrases[phrase.key]=phrase;if(!string.IsNullOrEmpty(phrase.english) && !englishKeys.ContainsKey(phrase.english))englishKeys[phrase.english]=phrase.key;} }
            catch(Exception e) {LastError=e.Message;}
            try
            {
                var chinese=NativeContent.Load<TextAsset>("NativeSettings/chinese");
                if(chinese==null)throw new InvalidOperationException("Missing Chinese translations resource");
                var catalog=JsonUtility.FromJson<ChineseCatalog>(chinese.text);
                foreach(var row in catalog.phrases)chinesePhrases[row.key]=row.value;
                foreach(var row in catalog.texts)chineseText[row.key]=row.value;
                chinesePrefixes=catalog.prefixes;chinesePatterns=catalog.patterns;
            }
            catch(Exception e){LastError=e.Message;}
            ReloadCustom();
        }
        public static string DefaultCustomPath()
        {
            string[] args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="--custom-language-file")return args[i+1];
            string original=Path.Combine(Application.dataPath,"customlang.csv");if(File.Exists(original))return original;
            string save=NativeSaveStore.FilePath;
            if(!string.IsNullOrEmpty(save)){string external=Path.Combine(Path.GetDirectoryName(save),"customlang.csv");if(File.Exists(external))return external;}
            return original;
        }
        public static bool ReloadCustom(string path=null)
        {
            Ensure(); path=path??DefaultCustomPath();
            if(!File.Exists(path)) { custom=new Dictionary<string,string>(StringComparer.Ordinal); LastError=""; return true; }
            try
            {
                var candidate=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(string line in File.ReadAllLines(path,new UTF8Encoding(false,true)))
                {
                    int comma=line.IndexOf(',');
                    if(comma<=0 || comma==line.Length-1) continue;
                    string key=line.Substring(0,comma),value=line.Substring(comma+1);
                    if(phrases.ContainsKey(key)) candidate[key]=value;
                }
                custom=candidate; LastError=""; return true;
            }
            catch(Exception e) {LastError=e.Message; return false;}
        }
        public static void ResetForVerification() { loaded=false; phrases.Clear(); englishKeys.Clear(); custom.Clear(); chinesePhrases.Clear();chineseText.Clear(); LastError=""; }
    }
}
