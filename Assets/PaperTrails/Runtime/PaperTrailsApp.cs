using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PaperTrails.Core;
using UnityEngine;

namespace PaperTrails
{
    public sealed class PaperTrailsApp : MonoBehaviour
    {
        enum ScreenMode { Menu, Lobby, Roulette, Match, Results, Collection }
        ScreenMode screen;
        GameSimulation game;
        GameView view;
        SoundBank sound;
        PreviewStudio studio;
        IGameSession network;
        bool online=true;
        string joinCode="";
        bool hosting,practice,remoteReady,ready,wasConnected,banked,authorized,smokeHost,codeWritten,recoveryCopied;
        int snapshots;
        Team team=Team.Red,remoteTeam=Team.Blue;
        int skin,remoteSkin,vote,remoteVote,wallet,localId,selectedArena;
        float accumulator,sendTimer,rouletteStart,nextClick,duration=300,unlockTime=-10,nextHeartbeat;
        int lastSecond=-1;
        int[] votes;
        string address="192.168.1.10",status="",remoteToken="",token,reveal="",recoveryCode="",restoreCode="",playerName="Player",remoteName="Player 2";
        readonly HashSet<int> unlocked=new HashSet<int>();
        readonly List<Texture2D> previews=new List<Texture2D>();
        Vector2 touchAnchor,gestureDirection,collectionScroll,lobbyScroll,rouletteScroll;
        bool touchActive;
        GUIStyle title,label,small,button,heading;
        float uiWidth,uiHeight,uiScale=1,suppressClickUntil;
        const int Cost=20;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){if(!FindAnyObjectByType<PaperTrailsApp>())new GameObject("PaperTrails").AddComponent<PaperTrailsApp>();}
        void Awake()
        {
            Application.targetFrameRate=60;UnityEngine.Screen.sleepTimeout=SleepTimeout.NeverSleep;
            UnityEngine.Screen.autorotateToPortrait=true;UnityEngine.Screen.autorotateToPortraitUpsideDown=true;UnityEngine.Screen.autorotateToLandscapeLeft=true;UnityEngine.Screen.autorotateToLandscapeRight=true;
            if(Application.isMobilePlatform)UnityEngine.Screen.orientation=ScreenOrientation.AutoRotation;
            view=gameObject.AddComponent<GameView>();sound=gameObject.AddComponent<SoundBank>();studio=gameObject.AddComponent<PreviewStudio>();
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.transform.rotation=Quaternion.Euler(55,-35,0);RenderSettings.ambientLight=new Color(.7f,.75f,.8f);
            wallet=PlayerPrefs.GetInt("coins",0);skin=PlayerPrefs.GetInt("skin",0);token=PlayerPrefs.GetString("token",Guid.NewGuid().ToString());PlayerPrefs.SetString("token",token);
            playerName=NormalizeName(PlayerPrefs.GetString("playerName","Player"),"Player");
            unlocked.Add(0);foreach(string s in PlayerPrefs.GetString("unlocked","0").Split(','))if(int.TryParse(s,out int id)&&id>=0&&id<SkinFactory.Names.Length)unlocked.Add(id);
            if(!unlocked.Contains(skin))skin=0;
            recoveryCode=CreateRecoveryCode();
            for(int i=0;i<10;i++)
            {
                var a=new Arena((ArenaKind)i);var tex=new Texture2D(Arena.Size,Arena.Size);var pixels=new Color32[a.Mask.Length];
                for(int c=0;c<pixels.Length;c++)pixels[c]=a.Mask[c]?new Color(.76f,.83f,.85f):new Color(0,0,0,0);
                tex.SetPixels32(pixels);tex.Apply();previews.Add(tex);
            }
            game=new GameSimulation(ArenaKind.Star,Team.Red,Team.Blue);view.Bind(game,0);
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-paperSmoke")>=0){practice=true;hosting=true;StartMatch((int)ArenaKind.Donut);game.Players[0].Connected=false;StartCoroutine(SmokeRoutine());}
            if(Array.IndexOf(args,"-paperHostSmoke")>=0){online=false;StartLobby(true,false);ready=true;smokeHost=true;Invoke(nameof(NetworkSmokeExit),25);}
            if(Array.IndexOf(args,"-paperClientSmoke")>=0){online=false;address="127.0.0.1";StartLobby(false,false);ready=true;Invoke(nameof(NetworkSmokeExit),21);}
            if(Array.IndexOf(args,"-paperRelayHostSmoke")>=0){online=true;StartLobby(true,false);ready=true;smokeHost=true;Invoke(nameof(NetworkSmokeExit),55);}
            int relayJoin=Array.IndexOf(args,"-paperRelayClientSmoke");
            if(relayJoin>=0&&relayJoin+1<args.Length){online=true;joinCode=args[relayJoin+1];StartLobby(false,false);ready=true;Invoke(nameof(NetworkSmokeExit),30);}
        }
        System.Collections.IEnumerator SmokeRoutine()
        {
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Screenshots",UnityEngine.Screen.width+"x"+UnityEngine.Screen.height));System.IO.Directory.CreateDirectory(path);
            yield return new WaitForSeconds(8);yield return CaptureFrame(System.IO.Path.Combine(path,"match.png"));
            StartMatch((int)ArenaKind.Cross);
            for(int i=0;i<game.Owners.Length;i++)
                if(game.Arena.Mask[i]&&!game.Arena.Protected(i,Team.Blue))game.Owners[i]=i%Arena.Size<40?Team.Red:Team.Neutral;
            foreach(Player player in game.Players){player.Alive=false;player.Respawn=999;}
            Player subject=game.Players[0];subject.Alive=true;subject.Connected=true;
            subject.X=38.5f;subject.Z=40.5f;subject.Cell=40*Arena.Size+38;
            subject.DX=subject.DesiredX=1;subject.DZ=subject.DesiredZ=0;
            for(int chunk=0;chunk<25;chunk++)game.DirtyChunks.Add(chunk);
            view.Bind(game,0);
            yield return new WaitForSeconds(.8f);game.SetDirection(0,0,1);
            yield return new WaitForSeconds(.65f);yield return CaptureFrame(System.IO.Path.Combine(path,"trail.png"));
            if(!subject.Alive||subject.TrailPath.Count<10)throw new InvalidOperationException("Curved trail smoke failed");
            yield return new WaitForSeconds(.5f);
            foreach(ScreenMode mode in new[]{ScreenMode.Menu,ScreenMode.Lobby,ScreenMode.Collection,ScreenMode.Roulette,ScreenMode.Results})
            {
                if(mode==ScreenMode.Roulette){votes=new[]{0,1,2,3,4,5,6,7,8,9};rouletteStart=Time.time;}
                if(mode==ScreenMode.Results){game.Winner=Team.Red;}
                screen=mode;yield return new WaitForSeconds(.6f);yield return CaptureFrame(System.IO.Path.Combine(path,mode.ToString()+".png"));yield return new WaitForSeconds(.4f);
            }
            Debug.Log("PAPERTRAILS_SMOKE_OK "+game.RedCount+":"+game.BlueCount);Application.Quit();
        }
        System.Collections.IEnumerator CaptureFrame(string path)
        {
            yield return new WaitForEndOfFrame();
            var texture=new Texture2D(UnityEngine.Screen.width,UnityEngine.Screen.height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);texture.Apply();
            System.IO.File.WriteAllBytes(path,texture.EncodeToPNG());Destroy(texture);
        }
        void NetworkSmokeExit(){bool success=screen==ScreenMode.Match&&(hosting?!string.IsNullOrEmpty(remoteToken):snapshots>20);Debug.Log("PAPERTRAILS_NETWORK_"+(success?"OK":"FAILED")+" snapshots="+snapshots+" host="+hosting);Application.Quit(success?0:1);}
        void Update()
        {
            PollNetwork();
            if(screen!=ScreenMode.Match&&Input.touchCount>0)
            {
                Touch touch=Input.GetTouch(0);
                if(touch.phase==TouchPhase.Moved&&Mathf.Abs(touch.deltaPosition.y)>3)
                {
                    float delta=touch.deltaPosition.y/uiScale;suppressClickUntil=Time.unscaledTime+.15f;
                    if(screen==ScreenMode.Lobby)lobbyScroll.y=Mathf.Max(0,lobbyScroll.y+delta);
                    if(screen==ScreenMode.Collection)collectionScroll.y=Mathf.Max(0,collectionScroll.y+delta);
                    if(screen==ScreenMode.Roulette)rouletteScroll.y=Mathf.Max(0,rouletteScroll.y+delta);
                }
            }
            if(smokeHost&&online&&!codeWritten&&network!=null&&!string.IsNullOrEmpty(network.JoinCode))
            {codeWritten=true;System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../relay-join-code.txt")),network.JoinCode);}
            if(smokeHost&&screen==ScreenMode.Lobby&&authorized&&remoteReady)Roulette();
            if(screen==ScreenMode.Roulette)
            {
                if(Time.time>=nextClick){sound.Play("roulette");nextClick=Time.time+Mathf.Lerp(.055f,.5f,(Time.time-rouletteStart)/4);}
                if(hosting && Time.time-rouletteStart>=4.5f)StartMatch(selectedArena);
            }
            if(screen!=ScreenMode.Match)return;
            int seconds=Mathf.CeilToInt(game.Remaining);if(seconds!=lastSecond){lastSecond=seconds;if(seconds<=10&&seconds>0)sound.Play("tick");}
            InputDirection();
            if(hosting)
            {
                game.Players[1].Connected=!practice && authorized && network!=null&&network.Connected;
                accumulator=Mathf.Min(accumulator+Time.deltaTime,.25f);
                while(accumulator>=GameSimulation.Tick){game.Step(GameSimulation.Tick);accumulator-=GameSimulation.Tick;}
                sendTimer-=Time.deltaTime;if(sendTimer<=0){sendTimer=.1f;Send(Packet.Snapshot(game));}
            }
            if(game.Phase==MatchPhase.Finished){Bank();screen=ScreenMode.Results;}
        }
        void InputDirection()
        {
            Vector2 dir=Vector2.zero;
            // Paper.io-style steering treats a drag as a small floating stick. Keep its
            // anchor stable until release so touch sampling noise cannot steer the player.
            float deadzone=Mathf.Max(10,Mathf.Min(UnityEngine.Screen.width,UnityEngine.Screen.height)*.014f);
            if(Input.touchCount>0)
            {
                Touch t=Input.GetTouch(0);
                if(t.phase==TouchPhase.Began){touchAnchor=t.position;touchActive=true;gestureDirection=Vector2.zero;}
                if(touchActive&&(t.phase==TouchPhase.Moved||t.phase==TouchPhase.Stationary))
                {
                    Vector2 delta=t.position-touchAnchor;
                    if(delta.sqrMagnitude>=deadzone*deadzone)gestureDirection=delta.normalized;
                }
                if(t.phase==TouchPhase.Ended||t.phase==TouchPhase.Canceled){touchActive=false;gestureDirection=Vector2.zero;}
                dir=gestureDirection;
            }
            else
            {
                Vector2 keys=new Vector2((Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0),(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1:0));
                if(Input.GetMouseButtonDown(0)){touchAnchor=Input.mousePosition;touchActive=true;gestureDirection=Vector2.zero;}
                if(touchActive&&Input.GetMouseButton(0))
                {
                    Vector2 delta=(Vector2)Input.mousePosition-touchAnchor;
                    if(delta.sqrMagnitude>=deadzone*deadzone)gestureDirection=delta.normalized;
                }
                if(Input.GetMouseButtonUp(0)){touchActive=false;gestureDirection=Vector2.zero;}
                dir=gestureDirection.sqrMagnitude>.5f?gestureDirection:keys;
            }
            if(dir.sqrMagnitude<.5f)return;
            dir.Normalize();if(hosting)game.SetDirection(0,dir.x,dir.y);else Send(new Packet{type="input",x=dir.x,z=dir.y});
        }
        void Send(Packet p){if(network!=null&&network.Connected)network.Send(PacketCodec.Encode(JsonUtility.ToJson(p)));}
        void PollNetwork()
        {
            if(network==null)return;
            if(network.Connected&&Time.unscaledTime>=nextHeartbeat){nextHeartbeat=Time.unscaledTime+1;Send(new Packet{type="ping"});}
            if(network.Connected&&!wasConnected)
            {authorized=false;if(!hosting)Send(new Packet{type="hello",token=token,name=playerName,team=(int)team,skin=skin,vote=vote,ready=ready});}
            if(!network.Connected&&wasConnected)
            {
                remoteReady=false;authorized=false;
                if(!hosting){Bank();screen=ScreenMode.Lobby;status="Host disconnected. Rejoin to resume if the host is still running.";}
            }
            wasConnected=network.Connected;
            int budget=32;
            while(budget-->0&&network.Incoming.TryDequeue(out string raw))
            {
                try
                {
                    Packet p=JsonUtility.FromJson<Packet>(PacketCodec.Decode(raw));if(p==null)continue;
                    if(hosting)
                    {
                        if(p.type=="hello")
                        {
                            authorized=false;
                            if(!string.IsNullOrEmpty(remoteToken)&&p.token!=remoteToken){Send(new Packet{type="error",message="This session is reserved for the original second player."});continue;}
                            if(string.IsNullOrEmpty(p.token))continue;
                            remoteToken=p.token;remoteName=NormalizeName(p.name,"Player 2");authorized=true;
                            if(screen==ScreenMode.Match||screen==ScreenMode.Results){Send(Packet.Snapshot(game));continue;}
                        }
                        if(!authorized)continue;
                        if((p.type=="hello"||p.type=="lobby")&&screen==ScreenMode.Lobby)
                        {remoteTeam=(Team)Mathf.Clamp(p.team,1,2);remoteSkin=Mathf.Clamp(p.skin,0,SkinFactory.Names.Length-1);remoteVote=Mathf.Clamp(p.vote,0,9);remoteReady=p.ready;SendLobby();}
                        if(p.type=="input"&&screen==ScreenMode.Match)game.SetDirection(1,p.x,p.z);
                    }
                    else
                    {
                        if(p.type=="lobby") {remoteName=NormalizeName(p.name,"Player");remoteTeam=(Team)Mathf.Clamp(p.team,1,2);remoteSkin=p.skin;remoteVote=p.vote;remoteReady=p.ready;duration=p.duration;if(screen!=ScreenMode.Lobby)screen=ScreenMode.Lobby;}
                        if(p.type=="roulette"&&p.votes!=null&&p.votes.Length==10){votes=p.votes;selectedArena=p.arena;rouletteStart=Time.time;screen=ScreenMode.Roulette;}
                        if(p.type=="state")ApplySnapshot(p);
                        if(p.type=="error")status=p.message;
                    }
                }
                catch(Exception e){status="Network data rejected: "+e.GetType().Name;}
            }
        }
        void ApplySnapshot(Packet p)
        {
            if(p.players==null||p.players.Length!=10||p.arena<0||p.arena>9)return;
            byte[] bytes=Convert.FromBase64String(p.owners);if(bytes.Length!=Arena.Size*Arena.Size)return;
            snapshots++;
            bool fresh=screen!=ScreenMode.Match&&screen!=ScreenMode.Results || (int)game.Arena.Kind!=p.arena;
            if(fresh){game=new GameSimulation((ArenaKind)p.arena,(Team)p.players[0].team,(Team)p.players[1].team);banked=false;}
            game.MatchId=p.matchId;
            for(int i=0;i<bytes.Length;i++)if(game.Owners[i]!=(Team)bytes[i]){game.Owners[i]=(Team)bytes[i];game.DirtyChunks.Add((i%Arena.Size)/16+(i/Arena.Size/16)*5);}
            if(!fresh)
            {
                Player old=game.Players[localId];WirePlayer incoming=p.players[localId];
                if(incoming.coins>old.Coins)sound.Play("coin");
                if(incoming.captured>old.Captured)sound.Play("capture",incoming.captured-old.Captured);
                if(incoming.cuts>old.Cuts)sound.Play("cut");
                if(incoming.alive!=old.Alive)sound.Play(incoming.alive?"respawn":"death");
                if(p.phase!=(int)game.Phase)sound.Play(p.phase==(int)MatchPhase.Overtime?"overtime":"finish");
            }
            for(int i=0;i<10;i++)p.players[i].Apply(game.Players[i]);
            game.Remaining=p.remaining;game.Phase=(MatchPhase)p.phase;game.Winner=(Team)p.winner;game.RedCount=p.red;game.BlueCount=p.blue;
            game.Coins[0].Clear();game.Coins[1].Clear();if(p.coins0!=null)game.Coins[0].AddRange(p.coins0);if(p.coins1!=null)game.Coins[1].AddRange(p.coins1);
            if(fresh)view.Bind(game,1);screen=game.Phase==MatchPhase.Finished?ScreenMode.Results:ScreenMode.Match;if(screen==ScreenMode.Results)Bank();
        }
        void SendLobby(){Send(new Packet{type="lobby",name=playerName,team=(int)team,skin=skin,vote=vote,ready=ready,duration=duration});}
        void StartLobby(bool host,bool offline)
        {
            network?.Dispose();network=null;wasConnected=false;hosting=host;practice=offline;localId=host?0:1;ready=false;remoteReady=false;remoteToken="";remoteName=offline?"CPU 1":host?"Player 2":"Player";status="";screen=ScreenMode.Lobby;
            if(offline){remoteReady=true;return;}
            network=NewSession();try{if(host)network.Host();else network.Join(online?joinCode:address);}catch(Exception e){status=e.Message;}
        }
        void Roulette()
        {
            votes=new int[10];votes[0]=vote;votes[1]=practice?UnityEngine.Random.Range(0,10):remoteVote;
            for(int i=2;i<10;i++)votes[i]=UnityEngine.Random.Range(0,10);
            selectedArena=votes[UnityEngine.Random.Range(0,10)];rouletteStart=Time.time;nextClick=0;screen=ScreenMode.Roulette;
            Send(new Packet{type="roulette",votes=votes,arena=selectedArena});
        }
        void StartMatch(int arena)
        {
            game=new GameSimulation((ArenaKind)arena,team,remoteTeam,Environment.TickCount,duration);game.Players[0].Skin=skin;game.Players[1].Skin=remoteSkin;
            game.Players[0].Name=hosting?playerName:remoteName;game.Players[1].Name=hosting?(practice?"CPU 1":remoteName):playerName;
            game.Event=(key,id,amount)=>{if(id==localId||key=="finish"||key=="overtime")sound.Play(key,amount);};
            view.Bind(game,localId);banked=false;accumulator=0;screen=ScreenMode.Match;Send(Packet.Snapshot(game));
        }
        void Bank()
        {
            if(banked||game==null)return;
            string key="reward."+game.MatchId;int paid=PlayerPrefs.GetInt(key,0),earned=game.Players[localId].Coins;
            wallet+=Mathf.Max(0,earned-paid);PlayerPrefs.SetInt(key,Mathf.Max(paid,earned));banked=true;Save();
        }
        void Save(){PlayerPrefs.SetInt("coins",wallet);PlayerPrefs.SetInt("skin",skin);PlayerPrefs.SetString("unlocked",string.Join(",",unlocked));PlayerPrefs.SetString("playerName",playerName);PlayerPrefs.Save();recoveryCode=CreateRecoveryCode();}
        static string NormalizeName(string value,string fallback)
        {
            string result=(value??"").Trim();if(result.Length>16)result=result.Substring(0,16);return string.IsNullOrEmpty(result)?fallback:result;
        }
        string CreateRecoveryCode()
        {
            var ids=new List<int>(unlocked);ids.Sort();
            string payload="PT1|"+wallet+"|"+skin+"|"+string.Join(",",ids);
            return ToBase64Url(payload+"|"+Checksum(payload));
        }
        static string ToBase64Url(string value){return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+','-').Replace('/','_');}
        static string DecodeBase64Url(string value)
        {
            string encoded=value.Trim().Replace('-','+').Replace('_','/');
            int remainder=encoded.Length%4;if(remainder==1)throw new FormatException();if(remainder==2)encoded+="==";else if(remainder==3)encoded+="=";
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        static string Checksum(string value)
        {
            unchecked{uint hash=2166136261u;for(int i=0;i<value.Length;i++)hash=(hash^value[i])*16777619u;return hash.ToString("X8");}
        }
        bool RestoreRecoveryCode()
        {
            try
            {
                string[] parts=DecodeBase64Url(restoreCode).Split('|');
                if(parts.Length!=5||parts[0]!="PT1")return false;
                string payload=parts[0]+"|"+parts[1]+"|"+parts[2]+"|"+parts[3];
                if(!string.Equals(parts[4],Checksum(payload),StringComparison.OrdinalIgnoreCase))return false;
                if(!int.TryParse(parts[1],out int restoredWallet)||restoredWallet<0)return false;
                if(!int.TryParse(parts[2],out int restoredSkin))return false;
                var restoredUnlocked=new HashSet<int>();
                foreach(string idText in parts[3].Split(','))if(!int.TryParse(idText,out int id)||id<0||id>=SkinFactory.Names.Length)return false;else restoredUnlocked.Add(id);
                if(!restoredUnlocked.Contains(0))return false;
                wallet=restoredWallet;unlocked.Clear();foreach(int id in restoredUnlocked)unlocked.Add(id);skin=unlocked.Contains(restoredSkin)?restoredSkin:0;
                Save();restoreCode="";reveal="Progress restored";unlockTime=-10;recoveryCopied=false;return true;
            }
            catch(Exception){return false;}
        }
        void ReturnLobby(){Bank();ready=false;remoteReady=practice;screen=ScreenMode.Lobby;if(hosting)SendLobby();}
        void OnDestroy(){network?.Dispose();}
        void OnApplicationPause(bool paused){if(paused)Save();}

        void Styles()
        {
            if(label!=null)return;
            label=new GUIStyle(GUI.skin.label){fontSize=20,wordWrap=true,normal={textColor=new Color(.92f,.95f,.97f)}};
            small=new GUIStyle(label){fontSize=15};heading=new GUIStyle(label){fontSize=27,fontStyle=FontStyle.Bold};title=new GUIStyle(label){fontSize=48,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){fontSize=19,padding=new RectOffset(12,12,8,8),border=new RectOffset(0,0,0,0)};
            button.normal.background=Solid(new Color(.16f,.2f,.22f));button.hover.background=Solid(new Color(.22f,.32f,.32f));button.active.background=Solid(new Color(.19f,.43f,.36f));
            GUI.skin.textField.fontSize=20;
        }
        void OnGUI()
        {
            Styles();Rect safe=UnityEngine.Screen.safeArea;float scale=Mathf.Min(safe.height/800f,safe.width/420f);uiScale=scale;uiWidth=safe.width/scale;uiHeight=safe.height/scale;
            GUI.matrix=Matrix4x4.TRS(new Vector3(safe.x,UnityEngine.Screen.height-safe.yMax,0),Quaternion.identity,new Vector3(scale,scale,1));
            if(screen==ScreenMode.Match){Hud();return;}
            Fill(new Rect(0,0,uiWidth,uiHeight),new Color(.055f,.075f,.09f,.96f));
            float w=Mathf.Min(uiWidth-40,960),x=(uiWidth-w)/2;
            GUI.Label(new Rect(x,25,w,65),"PaperTrails",title);
            GUI.Label(new Rect(x,90,w,30),"RED / BLUE     •     5 V 5",small);
            switch(screen)
            {
                case ScreenMode.Menu:
                    if(w>=600)GUI.DrawTexture(new Rect(x+w*.55f,190,w*.4f,w*.4f),previews[0],ScaleMode.ScaleToFit);
                    float bw=w<600?w:Mathf.Min(w*.48f,360);
                    GUI.Label(new Rect(x,120,bw,28),"Player name",small);
                    string typedName=GUI.TextField(new Rect(x,150,bw,42),playerName,16);if(typedName!=playerName){playerName=typedName;Save();}
                    if(Btn(new Rect(x,210,bw/2-4,40),online?"Online ✓":"Online"))online=true;
                    if(Btn(new Rect(x+bw/2+4,210,bw/2-4,40),online?"LAN":"LAN ✓"))online=false;
                    if(Btn(new Rect(x,268,bw,54),"Host game"))StartLobby(true,false);
                    GUI.Label(new Rect(x,338,bw,28),online?"Join code":"Host IP address",small);
                    if(online)joinCode=GUI.TextField(new Rect(x,370,bw,42),joinCode).ToUpperInvariant();else address=GUI.TextField(new Rect(x,370,bw,42),address);
                    if(Btn(new Rect(x,426,bw,54),"Join game"))StartLobby(false,false);
                    if(Btn(new Rect(x,504,bw,54),"Practice with CPUs"))StartLobby(true,true);
                    if(Btn(new Rect(x,580,bw,54),"Collection  /  "+wallet+" coins"))screen=ScreenMode.Collection;
                    GUI.Label(new Rect(x,uiHeight-50,w,32),online?"Online multiplayer":"LAN  /  "+LocalAddress(),small);break;
                case ScreenMode.Lobby: LobbyUI(x,w);break;
                case ScreenMode.Roulette:
                    GUI.Label(new Rect(x,145,w,42),"Arena roulette",heading);
                    int index=Time.time-rouletteStart>4?Array.IndexOf(votes,selectedArena):(int)((Time.time-rouletteStart)*12)%10;
                    int cols=w<600?2:5;float cw=(w-20)/cols;
                    rouletteScroll=GUI.BeginScrollView(new Rect(x,215,w,uiHeight-240),rouletteScroll,new Rect(0,0,w-20,Mathf.Ceil(10f/cols)*150));
                    for(int i=0;i<10;i++){var r=new Rect(i%cols*cw,i/cols*150,cw-10,140);Fill(r,i==index?new Color(.2f,.55f,.48f):new Color(.13f,.17f,.2f));GUI.DrawTexture(new Rect(r.x+15,r.y+8,r.width-30,90),previews[votes[i]],ScaleMode.ScaleToFit);GUI.Label(new Rect(r.x+8,r.y+104,r.width-16,35),ArenaName(votes[i]),small);}GUI.EndScrollView();break;
                case ScreenMode.Results:
                    GUI.contentColor=GameView.TeamColor(game.Winner);GUI.Label(new Rect(x,160,w,60),game.Winner.ToString().ToUpper()+" WINS",title);GUI.contentColor=Color.white;
                    GUI.Label(new Rect(x,240,w,90),$"Red {Percent(game.RedCount):0.0}%     Blue {Percent(game.BlueCount):0.0}%",heading);
                    Player p=game.Players[localId];GUI.Label(new Rect(x,340,w,140),$"Territory captured    {p.Captured} cells\nLargest capture        {p.Largest} cells\nTrail cuts   {p.Cuts}       Deaths   {p.Deaths}\nCoins collected    +{p.Coins}",label);
                    if(hosting&&Btn(new Rect(x,500,250,55),"Return to lobby"))ReturnLobby();
                    if(!hosting)GUI.Label(new Rect(x,500,w,45),"Waiting for host to return to lobby",label);
                    if(Btn(new Rect(x,575,250,55),"Main menu")){network?.Dispose();network=null;screen=ScreenMode.Menu;}break;
                case ScreenMode.Collection: CollectionUI(x,w);break;
            }
        }
        void LobbyUI(float x,float w)
        {
            if(w<600){PortraitLobby(x,w);return;}
            GUI.Label(new Rect(x,130,w,35),practice?"Practice lobby":hosting?"Host  /  "+SessionAddress():"Guest  /  "+(online?joinCode:address),heading);
            GUI.Label(new Rect(x,174,w,40),status!=""?status:practice?"Second human slot uses CPU control":network?.Status,small);
            GUI.contentColor=GameView.Red;if(Btn(new Rect(x,220,w*.23f,45),team==Team.Red?"Red  ✓":"Red")){team=Team.Red;ready=false;SendLobby();}
            GUI.contentColor=GameView.Blue;if(Btn(new Rect(x+w*.25f,220,w*.23f,45),team==Team.Blue?"Blue  ✓":"Blue")){team=Team.Blue;ready=false;SendLobby();}GUI.contentColor=Color.white;
            int red=(team==Team.Red?1:0)+(remoteTeam==Team.Red?1:0);
            GUI.Label(new Rect(x+w*.52f,220,w*.48f,60),$"Red: {red} human + {5-red} CPU\nBlue: {2-red} human + {3+red} CPU",small);
            if(Btn(new Rect(x,282,w*.48f,42),"Skin: "+SkinFactory.Names[skin])){var all=new List<int>(unlocked);all.Sort();skin=all[(all.IndexOf(skin)+1)%all.Count];Save();ready=false;SendLobby();}
            if(hosting){GUI.Label(new Rect(x+w*.52f,285,110,30),"Minutes",small);duration=GUI.HorizontalSlider(new Rect(x+w*.66f,302,w*.24f,20),duration,60,600);duration=Mathf.Round(duration/60)*60;GUI.Label(new Rect(x+w*.92f,285,50,30),(duration/60).ToString("0"),label);}
            GUI.Label(new Rect(x,342,w,34),"Your arena vote",label);
            for(int i=0;i<10;i++)
            {
                float cw=w/5;Rect r=new Rect(x+i%5*cw,382+i/5*104,cw-8,96);Fill(r,vote==i?new Color(.18f,.4f,.34f):new Color(.12f,.16f,.19f));
                GUI.DrawTexture(new Rect(r.x+5,r.y+3,r.width-10,62),previews[i],ScaleMode.ScaleToFit);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none)&&Time.unscaledTime>suppressClickUntil){vote=i;ready=false;SendLobby();}
                GUI.Label(new Rect(r.x+4,r.y+64,r.width-8,30),ArenaName(i),small);
            }
            if(Btn(new Rect(x,606,w*.28f,50),ready?"Ready ✓":"Ready")){ready=!ready;SendLobby();}
            GUI.Label(new Rect(x+w*.32f,616,w*.33f,35),remoteReady?"Partner ready":"Partner not ready",small);
            GUI.enabled=ready&&(practice||network!=null&&network.Connected&&remoteReady);
            if(hosting&&Btn(new Rect(x+w*.68f,606,w*.32f,50),"Start match"))Roulette();GUI.enabled=true;
            if(Btn(new Rect(x,680,160,42),"Back")){network?.Dispose();network=null;screen=ScreenMode.Menu;}
            if(!hosting&&network!=null&&!network.Connected&&Btn(new Rect(x+180,680,170,42),"Reconnect")){network.Dispose();network=NewSession();network.Join(online?joinCode:address);wasConnected=false;}
        }
        void PortraitLobby(float x,float w)
        {
            float inner=w-20,bottom=880;
            lobbyScroll=GUI.BeginScrollView(new Rect(x,130,w,uiHeight-150),lobbyScroll,new Rect(0,0,inner,1060));
            GUI.Label(new Rect(0,0,inner,36),practice?"Practice lobby":hosting?"Host lobby":"Join lobby",heading);
            GUI.Label(new Rect(0,42,inner,42),status!=""?status:practice?"Second slot: CPU":hosting?SessionAddress()+" / "+network?.Status:network?.Status,small);
            GUI.contentColor=GameView.Red;if(Btn(new Rect(0,92,inner/2-5,48),team==Team.Red?"Red ✓":"Red")){team=Team.Red;ready=false;SendLobby();}
            GUI.contentColor=GameView.Blue;if(Btn(new Rect(inner/2+5,92,inner/2-5,48),team==Team.Blue?"Blue ✓":"Blue")){team=Team.Blue;ready=false;SendLobby();}GUI.contentColor=Color.white;
            int red=(team==Team.Red?1:0)+(remoteTeam==Team.Red?1:0);GUI.Label(new Rect(0,152,inner,52),$"Red: {red} human + {5-red} CPU\nBlue: {2-red} human + {3+red} CPU",small);
            if(Btn(new Rect(0,214,inner,46),"Skin: "+SkinFactory.Names[skin])){var all=new List<int>(unlocked);all.Sort();skin=all[(all.IndexOf(skin)+1)%all.Count];Save();ready=false;SendLobby();}
            GUI.Label(new Rect(0,272,inner,30),"Match: "+(duration/60).ToString("0")+" minutes",small);
            if(hosting){duration=GUI.HorizontalSlider(new Rect(150,282,inner-150,20),duration,60,600);duration=Mathf.Round(duration/60)*60;}
            GUI.Label(new Rect(0,310,inner,30),"Your arena vote",label);
            for(int i=0;i<10;i++)
            {
                Rect r=new Rect(i%2*inner/2,350+i/2*104,inner/2-8,96);Fill(r,vote==i?new Color(.18f,.4f,.34f):new Color(.12f,.16f,.19f));GUI.DrawTexture(new Rect(r.x+5,r.y+3,r.width-10,62),previews[i],ScaleMode.ScaleToFit);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none)&&Time.unscaledTime>suppressClickUntil){vote=i;ready=false;SendLobby();}GUI.Label(new Rect(r.x+4,r.y+64,r.width-8,30),ArenaName(i),small);
            }
            if(Btn(new Rect(0,bottom,inner*.46f,48),ready?"Ready ✓":"Ready")){ready=!ready;SendLobby();}
            GUI.Label(new Rect(inner*.5f,bottom+5,inner*.5f,44),remoteReady?"Partner ready":"Partner not ready",small);
            GUI.enabled=ready&&(practice||network!=null&&network.Connected&&remoteReady);if(hosting&&Btn(new Rect(0,bottom+60,inner,48),"Start match"))Roulette();GUI.enabled=true;
            if(Btn(new Rect(0,bottom+120,inner*.46f,44),"Back")){network?.Dispose();network=null;screen=ScreenMode.Menu;}
            if(!hosting&&network!=null&&!network.Connected&&Btn(new Rect(inner*.5f,bottom+120,inner*.5f,44),"Reconnect")){network.Dispose();network=NewSession();network.Join(online?joinCode:address);wasConnected=false;}
            GUI.EndScrollView();
        }
        void CollectionUI(float x,float w)
        {
            bool narrow=w<600;
            if(string.IsNullOrEmpty(recoveryCode))recoveryCode=CreateRecoveryCode();
            GUI.Label(new Rect(x,140,w,40),narrow?$"Collection  /  {wallet} coins":$"Capsule collection     {wallet} coins",heading);
            GUI.DrawTexture(narrow?new Rect(x,180,w*.46f,160):new Rect(x+w*.62f,120,w*.36f,205),studio.Machine(Time.time-unlockTime),ScaleMode.ScaleToFit);
            GUI.enabled=wallet>=Cost&&unlocked.Count<SkinFactory.Names.Length&&Time.time-unlockTime>=2;
            if(Btn(narrow?new Rect(x+w*.49f,220,w*.51f,60):new Rect(x,200,Mathf.Min(w,400),55),narrow?$"Turn / {Cost} coins":$"Turn capsule machine  /  {Cost} coins"))
            {
                var locked=new List<int>();for(int i=0;i<SkinFactory.Names.Length;i++)if(!unlocked.Contains(i))locked.Add(i);
                int choice=locked[UnityEngine.Random.Range(0,locked.Count)];wallet-=Cost;unlocked.Add(choice);skin=choice;reveal=SkinFactory.Names[choice]+" unlocked";unlockTime=Time.time;Save();sound.Play("unlock");
            }
            GUI.enabled=true;GUI.Label(new Rect(x,narrow?345:270,narrow?w:w*.6f,38),Time.time-unlockTime>=2?reveal:"Opening capsule...",label);
            float codeY=narrow?385:325;
            GUI.Label(new Rect(x,codeY,w*.72f,22),"Recovery code",small);
            GUI.Label(new Rect(x,codeY+19,w*.74f,36),recoveryCode,new GUIStyle(small){fontSize=narrow?10:12,wordWrap=false});
            if(Btn(new Rect(x+w*.76f,codeY+16,w*.24f,36),"Copy")){GUIUtility.systemCopyBuffer=recoveryCode;recoveryCopied=true;}
            GUI.Label(new Rect(x,codeY+56,w*.72f,22),recoveryCopied?"Copied to clipboard":"Keep this code somewhere safe",small);
            restoreCode=GUI.TextField(new Rect(x,codeY+80,w*.74f,38),restoreCode);
            if(Btn(new Rect(x+w*.76f,codeY+80,w*.24f,38),"Restore"))if(!RestoreRecoveryCode())reveal="Invalid recovery code";
            float gridTop=narrow?589:529;
            Fill(new Rect(x,gridTop-68,w,68),new Color(.07f,.10f,.13f,.95f));
            GUI.DrawTexture(new Rect(x+6,gridTop-64,60,60),studio.BigIcon(skin),ScaleMode.ScaleToFit);
            GUI.Label(new Rect(x+74,gridTop-66,w-80,20),"NOW USING",small);
            GUI.Label(new Rect(x+74,gridTop-46,w-80,40),SkinFactory.Names[skin],new GUIStyle(heading){fontSize=narrow?20:24});
            Rect area=new Rect(x,gridTop,w,uiHeight-gridTop-100);int columns=narrow?2:3;
            collectionScroll=GUI.BeginScrollView(area,collectionScroll,new Rect(0,0,w-25,Mathf.Ceil(SkinFactory.Names.Length/(float)columns)*52));
            for(int i=0;i<SkinFactory.Names.Length;i++)
            {GUI.enabled=unlocked.Contains(i);float cx=i%columns*(w-25)/columns,cy=i/columns*52,cellWidth=(w-25)/columns-8;var cell=new Rect(cx,cy,cellWidth,44);if(i==skin)Fill(cell,new Color(.95f,.73f,.16f,.22f));if(Btn(cell,"")){skin=i;Save();}GUI.DrawTexture(new Rect(cx+2,cy,40,40),studio.Icon(i),ScaleMode.ScaleToFit);GUI.Label(new Rect(cx+44,cy+6,cellWidth-46,36),(skin==i?"✓ ":"")+SkinFactory.Names[i],new GUIStyle(small){fontSize=narrow?13:16});}GUI.enabled=true;GUI.EndScrollView();
            if(Btn(new Rect(x,uiHeight-75,200,45),"Back"))screen=ScreenMode.Menu;
        }
        void Hud()
        {
            bool portrait=uiWidth<600;float scoreY=portrait?59:20;
            Fill(new Rect(0,0,uiWidth,portrait?110:76),new Color(.045f,.06f,.08f,.9f));
            GUI.contentColor=GameView.Red;GUI.Label(new Rect(16,scoreY,uiWidth*.46f,40),$"RED  {Percent(game.RedCount):0.0}%",portrait?label:heading);
            GUI.contentColor=GameView.Blue;GUI.Label(new Rect(uiWidth*(portrait?.53f:.7f),scoreY,uiWidth*(portrait?.46f:.3f)-10,40),$"BLUE  {Percent(game.BlueCount):0.0}%",portrait?label:heading);GUI.contentColor=Color.white;
            string time=game.Phase==MatchPhase.Overtime?"OVERTIME":TimeSpan.FromSeconds(Mathf.Ceil(game.Remaining)).ToString(@"m\:ss");
            var centered=new GUIStyle(heading){alignment=TextAnchor.MiddleCenter};GUI.Label(new Rect(uiWidth*.3f,14,uiWidth*.4f,48),time,centered);
            Fill(new Rect(12,portrait?122:88,145,42),new Color(.045f,.06f,.08f,.85f));GUI.Label(new Rect(20,portrait?126:92,125,35),game.Players[localId].Coins+" coins",label);
            float size=Mathf.Min(190,uiWidth*.28f);Rect map=new Rect(18,uiHeight-size-18,size,size);Fill(map,new Color(.055f,.08f,.1f,.88f));GUI.DrawTexture(map,view.Map);
            foreach(Player p in game.Players)
            {
                if(!p.Alive)continue;float px=map.x+p.X/Arena.Size*size,py=map.yMax-p.Z/Arena.Size*size;
                Fill(new Rect(px-3,py-3,6,6),GameView.TeamColor(p.Team));
                if(p.Human){GUI.DrawTexture(new Rect(px-12,py-12,24,24),studio.Icon(p.Skin),ScaleMode.ScaleToFit);GUI.Label(new Rect(px-40,py-25,100,22),NormalizeName(p.Name,p.Id==localId?"YOU":"P"+(p.Id+1)),new GUIStyle(small){fontSize=11,normal={textColor=Color.white}});}
            }
            for(int t=0;t<2;t++){int h=game.Arena.Hubs[t];GUI.Label(new Rect(map.x+(h%Arena.Size)/80f*size-5,map.yMax-(h/Arena.Size)/80f*size-10,20,20),"H",small);}
            Player me=game.Players[localId];if(!me.Alive){Fill(new Rect(uiWidth/2-170,uiHeight/2-50,340,100),new Color(.05f,.07f,.09f,.93f));GUI.Label(new Rect(uiWidth/2-160,uiHeight/2-25,320,60),$"Respawning  {me.Respawn:0.0}",centered);}
        }
        float Percent(int count)=>100f*count/game.Arena.Claimable;
        static string ArenaName(int id)=>id==4?"Crescent Moon":((ArenaKind)id).ToString();
        bool Btn(Rect r,string text)=>GUI.Button(r,text,button)&&Time.unscaledTime>suppressClickUntil;
        IGameSession NewSession()=>online?(IGameSession)new RelaySession():new LanSession();
        string SessionAddress()=>online?"Code "+network?.JoinCode:LocalAddress();
        static Texture2D Solid(Color color){var t=new Texture2D(1,1);t.SetPixel(0,0,color);t.Apply();return t;}
        static void Fill(Rect r,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        static string LocalAddress(){try{foreach(var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)if(ip.AddressFamily==AddressFamily.InterNetwork)return ip.ToString();}catch(Exception){}return "127.0.0.1";}
    }
}
