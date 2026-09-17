using System.Collections.Generic;
using UnityEngine;

namespace PaperTrails
{
    public sealed class SoundBank : MonoBehaviour
    {
        AudioSource source;
        readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        void Awake()
        {
            source=gameObject.AddComponent<AudioSource>();source.volume=.28f;
            string[] names={"coin","capture","cut","death","respawn","overtime","finish","roulette","unlock","tick"};
            for(int k=0;k<names.Length;k++)
            {
                int count=names[k]=="finish"?22050:7000;var samples=new float[count];
                for(int i=0;i<count;i++){float t=i/22050f,env=Mathf.Pow(1-i/(float)count,2);float f=names[k]=="death"?250-t*450:330+k*47+(names[k]=="coin"?t*1400:0);samples[i]=Mathf.Sin(2*Mathf.PI*f*t)*env*.45f;}
                var clip=AudioClip.Create(names[k],count,1,22050,false);clip.SetData(samples,0);clips[names[k]]=clip;
            }
        }
        public void Play(string key,int amount=0)
        {if(clips.TryGetValue(key,out var clip)){source.pitch=key=="capture"?Mathf.Clamp(1+amount/200f,1,1.7f):1;source.PlayOneShot(clip);}}
    }
}
