using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace PaperTrails.Editor
{
    // Adds the iOS local-network usage key after every iOS build. Without it,
    // iOS 14+ silently blocks the game's LAN mode (direct TCP to a host IP).
    // Implemented with plain XML so it compiles with or without the iOS
    // Build Support module installed (no UnityEditor.iOS.Extensions dep).
    public static class IosPlistPostProcess
    {
        [PostProcessBuild(100)]
        public static void OnPostProcess(BuildTarget target,string path)
        {
            if(target!=BuildTarget.iOS)return;
            string plist=Path.Combine(path,"Info.plist");
            if(!File.Exists(plist)){Debug.LogWarning("PAPERTRAILS_PLIST missing "+plist);return;}
            try
            {
                var doc=new XmlDocument{XmlResolver=null};
                doc.Load(plist);
                var dict=doc.SelectSingleNode("plist/dict");
                if(dict==null){Debug.LogWarning("PAPERTRAILS_PLIST no root dict");return;}
                SetString(doc,dict,"NSLocalNetworkUsageDescription","PaperTrails uses the local network to host and join LAN matches with nearby devices. Online Relay play does not need this.");
                doc.Save(plist);
                Debug.Log("PAPERTRAILS_PLIST_OK "+plist);
            }
            catch(System.Exception e){Debug.LogWarning("PAPERTRAILS_PLIST failed: "+e.Message);}
        }
        static void SetString(XmlDocument doc,XmlNode dict,string key,string value)
        {
            foreach(XmlNode child in dict.ChildNodes)
            {
                if(child.NodeType==XmlNodeType.Element&&child.Name=="key"&&child.InnerText==key)
                {
                    XmlNode next=child.NextSibling;
                    while(next!=null&&next.NodeType!=XmlNodeType.Element)next=next.NextSibling;
                    if(next!=null&&next.Name=="string"){next.InnerText=value;return;}
                    var val=doc.CreateElement("string");val.InnerText=value;
                    dict.InsertAfter(val,child);return;
                }
            }
            var keyElement=doc.CreateElement("key");keyElement.InnerText=key;
            var valElement=doc.CreateElement("string");valElement.InnerText=value;
            dict.AppendChild(keyElement);dict.AppendChild(valElement);
        }
    }
}
