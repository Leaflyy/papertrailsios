using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaperTrails
{
    public static class PacketCodec
    {
        public static string Encode(string json)
        {
            using(var output=new MemoryStream())
            {
                using(var zip=new DeflateStream(output,CompressionLevel.Fastest,true))
                {byte[] bytes=Encoding.UTF8.GetBytes(json);zip.Write(bytes,0,bytes.Length);}
                return Convert.ToBase64String(output.ToArray());
            }
        }
        public static string Decode(string packet)
        {
            using(var input=new MemoryStream(Convert.FromBase64String(packet)))
            using(var zip=new DeflateStream(input,CompressionMode.Decompress))
            using(var output=new MemoryStream())
            {
                var buffer=new byte[4096];int count;
                while((count=zip.Read(buffer,0,buffer.Length))>0){if(output.Length+count>1024*1024)throw new InvalidDataException("Packet exceeds limit");output.Write(buffer,0,count);}
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
