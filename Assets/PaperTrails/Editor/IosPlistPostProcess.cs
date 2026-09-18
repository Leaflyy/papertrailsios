using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace PaperTrails.Editor
{
    // Adds the iOS local-network usage key after every iOS build. Without it,
    // iOS 14+ silently blocks the game's LAN mode (direct TCP to a host IP).
    // Implemented as byte-preserving text surgery (NOT XmlDocument.Save):
    // a full DOM re-serialization once produced output that Xcode's strict
    // plist parser rejected, failing the archive. We only insert/replace the
    // one key, keep the original encoding/BOM/doctype/whitespace untouched,
    // verify the result still parses, and write nothing on any doubt so a
    // post-process hiccup can never break the build.
    // No UnityEditor.iOS.Extensions dependency: compiles with or without the
    // iOS Build Support module installed.
    public static class IosPlistPostProcess
    {
        const string Key="NSLocalNetworkUsageDescription";
        const string Value="PaperTrails uses the local network to host and join LAN matches with nearby devices. Online Relay play does not need this.";
        const string UrlBlock="\t<key>CFBundleURLTypes</key>\n\t<array>\n\t\t<dict>\n\t\t\t<key>CFBundleURLSchemes</key>\n\t\t\t<array>\n\t\t\t\t<string>papertrails</string>\n\t\t\t</array>\n\t\t</dict>\n\t</array>\n";
        [PostProcessBuild(100)]
        public static void OnPostProcess(BuildTarget target,string path)
        {
            if(target!=BuildTarget.iOS)return;
            string plist=Path.Combine(path,"Info.plist");
            if(!File.Exists(plist)){Debug.LogWarning("PAPERTRAILS_PLIST missing "+plist);return;}
            try
            {
                byte[] raw=File.ReadAllBytes(plist);
                bool hasBom=raw.Length>=3&&raw[0]==0xEF&&raw[1]==0xBB&&raw[2]==0xBF;
                string text=Encoding.UTF8.GetString(raw,hasBom?3:0,raw.Length-(hasBom?3:0));
                string escaped=Value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
                string updated=Upsert(text,escaped);
                if(updated==null){Debug.LogWarning("PAPERTRAILS_PLIST no root dict, left untouched");return;}
                if(updated.IndexOf("<key>CFBundleURLTypes</key>")<0)
                {
                    int close=updated.LastIndexOf("</dict>");
                    if(close<0){Debug.LogWarning("PAPERTRAILS_PLIST no root dict, left untouched");return;}
                    updated=updated.Substring(0,close)+UrlBlock+updated.Substring(close);
                }
                try{var check=new XmlDocument{XmlResolver=null};check.LoadXml(updated);}
                catch(System.Exception e){Debug.LogWarning("PAPERTRAILS_PLIST verification failed, left untouched: "+e.Message);return;}
                byte[] body=Encoding.UTF8.GetBytes(updated);
                byte[] output;
                if(hasBom){output=new byte[body.Length+3];output[0]=0xEF;output[1]=0xBB;output[2]=0xBF;System.Buffer.BlockCopy(body,0,output,3,body.Length);}
                else output=body;
                File.WriteAllBytes(plist,output);
                Debug.Log("PAPERTRAILS_PLIST_OK "+plist);
            }
            catch(System.Exception e){Debug.LogWarning("PAPERTRAILS_PLIST failed: "+e.Message);}
        }
        static string Upsert(string text,string escapedValue)
        {
            var existing=new Regex("<key>"+Key+"</key>\\s*<string>.*?</string>",RegexOptions.Singleline);
            if(existing.IsMatch(text))return existing.Replace(text,"<key>"+Key+"</key>\n\t<string>"+escapedValue+"</string>",1);
            int close=text.LastIndexOf("</dict>");
            if(close<0)return null;
            return text.Substring(0,close)+"\t<key>"+Key+"</key>\n\t<string>"+escapedValue+"</string>\n"+text.Substring(close);
        }
    }
}
