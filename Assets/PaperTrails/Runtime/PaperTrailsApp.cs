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
        static readonly int ArenaCount=Enum.GetValues(typeof(ArenaKind)).Length;
        static readonly string[] ArenaNames={"Star","USA","Donut","Cross","Crescent Moon","Skull","Hourglass","Butterfly","Heart","Spiral","Diamond","Lightning Bolt","Mustache","Flare","Cash","Tilda","Triangle","Circle","Square","Parallelogram","Cool S","Cubic Plane Curve"};
        ScreenMode screen;
        GameSimulation game;
        GameView view;
        SoundBank sound;
        PreviewStudio studio;
        IGameSession network;
        bool online=true;
        string joinCode="";
        bool hosting,practice,remoteReady,ready,wasConnected,banked,authorized,smokeHost,codeWritten,recoveryCopied,reconnecting,lastOnline=true;
        int snapshots;
        Team team=Team.Red,remoteTeam=Team.Blue;
        int skin,remoteSkin,vote,remoteVote,wallet,localId,selectedArena,sendSeq;
        readonly SnapshotChunks.Assembler snapshotAsm=new SnapshotChunks.Assembler();
        float accumulator,sendTimer,finishSend,rouletteStart,nextClick,duration=300,unlockTime=-10,nextHeartbeat,reconnectUntil,lastRxTime;
        int lastSecond=-1,spinStep,spinTotal;
        int[] votes;
        string address="192.168.1.10",status="",remoteToken="",token,reveal="",recoveryCode="",restoreCode="",playerName="Player",remoteName="Player 2",lastCode="",lastAddress="",pendingLink=null;
        readonly HashSet<int> unlocked=new HashSet<int>();
        readonly List<Texture2D> previews=new List<Texture2D>();
        Vector2 touchAnchor,gestureDirection,collectionScroll,lobbyScroll,rouletteScroll,voteScroll;
        bool touchActive;
        GUIStyle title,label,small,button,heading,btnRed,btnGreen,btnBlue,btnGray,btnOff,chip,billboard,sliderBack,sliderKnob,panelS,cardS,chipS;
        float uiWidth,uiHeight,uiScale=1,suppressClickUntil;
        float countdownEnd=-10;int lastCount;bool rouletteLocked;
        const int Cost=20;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot(){if(!FindAnyObjectByType<PaperTrailsApp>())new GameObject("PaperTrails").AddComponent<PaperTrailsApp>();}
        void Awake()
        {
            Application.targetFrameRate=60;Application.runInBackground=true;UnityEngine.Screen.sleepTimeout=SleepTimeout.NeverSleep;
            UnityEngine.Screen.autorotateToPortrait=true;UnityEngine.Screen.autorotateToPortraitUpsideDown=true;UnityEngine.Screen.autorotateToLandscapeLeft=true;UnityEngine.Screen.autorotateToLandscapeRight=true;
            if(Application.isMobilePlatform)UnityEngine.Screen.orientation=ScreenOrientation.AutoRotation;
            view=gameObject.AddComponent<GameView>();sound=gameObject.AddComponent<SoundBank>();studio=gameObject.AddComponent<PreviewStudio>();
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.transform.rotation=Quaternion.Euler(55,-35,0);RenderSettings.ambientLight=new Color(.7f,.75f,.8f);
            wallet=PlayerPrefs.GetInt("coins",0);skin=PlayerPrefs.GetInt("skin",0);token=PlayerPrefs.GetString("token",Guid.NewGuid().ToString());PlayerPrefs.SetString("token",token);
            playerName=NormalizeName(PlayerPrefs.GetString("playerName","Player"),"Player");
            unlocked.Add(0);foreach(string s in PlayerPrefs.GetString("unlocked","0").Split(','))if(int.TryParse(s,out int id)&&id>=0&&id<SkinFactory.Names.Length)unlocked.Add(id);
            if(!unlocked.Contains(skin))skin=0;
            recoveryCode=CreateRecoveryCode();
            for(int i=0;i<ArenaCount;i++)
            {
                var a=new Arena((ArenaKind)i);var tex=new Texture2D(Arena.Size,Arena.Size);var pixels=new Color32[a.Mask.Length];
                for(int c=0;c<pixels.Length;c++)pixels[c]=a.Mask[c]?new Color(.76f,.83f,.85f):new Color(0,0,0,0);
                tex.SetPixels32(pixels);tex.Apply();previews.Add(tex);
            }
            game=new GameSimulation(ArenaKind.Star,Team.Red,Team.Blue);view.Bind(game,0);
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-paperSmoke")>=0){practice=true;hosting=true;StartMatch((int)ArenaKind.Donut);countdownEnd=-10;lastCount=0;game.Players[0].Connected=false;StartCoroutine(SmokeRoutine());}
            if(Array.IndexOf(args,"-paperHostSmoke")>=0){online=false;StartLobby(true,false);ready=true;smokeHost=true;Invoke(nameof(NetworkSmokeExit),25);}
            if(Array.IndexOf(args,"-paperClientSmoke")>=0){online=false;address="127.0.0.1";StartLobby(false,false);ready=true;Invoke(nameof(NetworkSmokeExit),21);}
            if(Array.IndexOf(args,"-paperRelayHostSmoke")>=0){online=true;StartLobby(true,false);ready=true;smokeHost=true;Invoke(nameof(NetworkSmokeExit),55);}
            int relayJoin=Array.IndexOf(args,"-paperRelayClientSmoke");
            if(relayJoin>=0&&relayJoin+1<args.Length){online=true;joinCode=args[relayJoin+1];StartLobby(false,false);ready=true;Invoke(nameof(NetworkSmokeExit),30);}
            Application.deepLinkActivated+=OnDeepLink;
            if(!string.IsNullOrEmpty(Application.absoluteURL))pendingLink=Application.absoluteURL;
        }
        System.Collections.IEnumerator SmokeRoutine()
        {
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../Screenshots",UnityEngine.Screen.width+"x"+UnityEngine.Screen.height));System.IO.Directory.CreateDirectory(path);
            yield return new WaitForSeconds(8);yield return CaptureFrame(System.IO.Path.Combine(path,"match.png"));
            StartMatch((int)ArenaKind.Cross);countdownEnd=-10;lastCount=0;
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
                if(mode==ScreenMode.Roulette){votes=new int[ArenaCount];for(int vi=0;vi<ArenaCount;vi++)votes[vi]=vi;rouletteStart=Time.time;}
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
            if(screen==ScreenMode.Menu&&!string.IsNullOrEmpty(pendingLink)){string url=pendingLink;pendingLink=null;HandleLink(url);}
            if(reconnecting&&Time.unscaledTime>=reconnectUntil){reconnecting=false;network?.Dispose();network=null;if(screen==ScreenMode.Match)screen=ScreenMode.Lobby;status="Could not reconnect. Rejoin with the code or link.";}
            if(!hosting&&screen==ScreenMode.Match&&!reconnecting&&network!=null&&network.Connected&&Time.unscaledTime-lastRxTime>5f){Bank();BeginReconnect();}
            PollNetwork();
            if(screen!=ScreenMode.Match&&Input.touchCount>0)
            {
                Touch touch=Input.GetTouch(0);
                if(touch.phase==TouchPhase.Moved&&Mathf.Abs(touch.deltaPosition.y)>3)
                {
                    float delta=touch.deltaPosition.y/uiScale;suppressClickUntil=Time.unscaledTime+.15f;
                    if(screen==ScreenMode.Collection)collectionScroll.y=Mathf.Max(0,collectionScroll.y+delta);
                }
            }
            if(smokeHost&&online&&!codeWritten&&network!=null&&!string.IsNullOrEmpty(network.JoinCode))
            {codeWritten=true;System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../relay-join-code.txt")),network.JoinCode);}
            if(smokeHost&&screen==ScreenMode.Lobby&&authorized&&remoteReady)Roulette();
            if(screen==ScreenMode.Roulette)
            {
                float rt=Time.time-rouletteStart;
                // One easing clock drives both: each step advances one cell
                // and plays one tick, intervals stretching cube-root style so
                // ticks and selection slow down together and land on the
                // winner exactly at 3s.
                while(spinStep<spinTotal&&Time.time>=nextClick)
                {
                    spinStep++;sound.Play("roulette");
                    if(spinStep>=spinTotal){rouletteLocked=true;sound.Play("tick");}
                    else{float frac=(spinStep+1)/(float)spinTotal;nextClick=rouletteStart+3*(1-Mathf.Pow(1-frac,1f/3f));}
                }
                if(hosting && rt>=5f)StartMatch(selectedArena);
            }
            if(screen!=ScreenMode.Match)
            {
                // After regulation ends, keep the final snapshots flowing briefly so a
                // joiner that missed the exact finish frame still banks its coins.
                if(hosting&&screen==ScreenMode.Results&&finishSend>0&&game!=null){finishSend-=Time.deltaTime;sendTimer-=Time.deltaTime;if(sendTimer<=0){sendTimer=.1f;SendUnreliable(Packet.Snapshot(game));}}
                return;
            }
            int seconds=Mathf.CeilToInt(game.Remaining);if(seconds!=lastSecond){lastSecond=seconds;if(seconds<=10&&seconds>0)sound.Play("tick");}
            float remain=countdownEnd-Time.unscaledTime;
            if(remain>0){int n=Mathf.CeilToInt(remain);if(n!=lastCount){lastCount=n;sound.Play("tick");}}
            else if(lastCount>0){lastCount=0;sound.Play("go");}
            bool frozen=remain>0;
            if(!frozen)InputDirection();
            if(hosting)
            {
                game.Players[1].Connected=!practice && authorized && network!=null&&network.Connected;
                if(!frozen){accumulator=Mathf.Min(accumulator+Time.deltaTime,.25f);while(accumulator>=GameSimulation.Tick){game.Step(GameSimulation.Tick);accumulator-=GameSimulation.Tick;}}
                else accumulator=0;
                sendTimer-=Time.deltaTime;if(sendTimer<=0){sendTimer=.1f;SendUnreliable(Packet.Snapshot(game));}
            }
            if(game.Phase==MatchPhase.Finished){if(hosting&&screen==ScreenMode.Match)finishSend=2f;Bank();screen=ScreenMode.Results;}
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
            dir.Normalize();if(hosting)game.SetDirection(0,dir.x,dir.y);else SendUnreliable(new Packet{type="input",x=dir.x,z=dir.y});
        }
        void Send(Packet p){if(network!=null&&network.Connected)network.Send(PacketCodec.Encode(JsonUtility.ToJson(p)));}
        void SendUnreliable(Packet p)
        {
            if(network==null||!network.Connected)return;
            p.seq=++sendSeq;
            string raw=PacketCodec.Encode(JsonUtility.ToJson(p));
            if(raw.Length<=SnapshotChunks.MaxPiece){network.SendUnreliable(raw);return;}
            foreach(string chunk in SnapshotChunks.Split(sendSeq,raw))network.SendUnreliable(chunk);
        }
        void PollNetwork()
        {
            if(network==null)return;
            if(network.Connected&&Time.unscaledTime>=nextHeartbeat){nextHeartbeat=Time.unscaledTime+1;Send(new Packet{type="ping"});}
            if(network.Connected&&!wasConnected)
            {authorized=false;lastRxTime=Time.unscaledTime;if(!hosting)Send(new Packet{type="hello",token=token,name=playerName,team=(int)team,skin=skin,vote=vote,ready=ready});}
            if(!network.Connected&&wasConnected)
            {
                remoteReady=false;authorized=false;
                if(!hosting){Bank();BeginReconnect();}
            }
            wasConnected=network.Connected;
            int budget=32;
            while(budget-->0&&network.Incoming.TryDequeue(out string raw))
            {
                try
                {
                    string encoded=raw;
                    if(encoded.StartsWith("CH|")){if(!snapshotAsm.Push(encoded,out encoded))continue;}
                    Packet p=JsonUtility.FromJson<Packet>(PacketCodec.Decode(encoded));if(p==null)continue;lastRxTime=Time.unscaledTime;
                    if(hosting)
                    {
                        if(p.type=="hello")
                        {
                            authorized=false;
                            if(!string.IsNullOrEmpty(remoteToken)&&p.token!=remoteToken){Send(new Packet{type="error",message="This session is reserved for the original second player."});continue;}
                            if(string.IsNullOrEmpty(p.token))continue;
                            remoteToken=p.token;remoteName=NormalizeName(p.name,"Player 2");authorized=true;
                            if(screen==ScreenMode.Match||screen==ScreenMode.Results){SendUnreliable(Packet.Snapshot(game));continue;}
                        }
                        if(!authorized)continue;
                        if((p.type=="hello"||p.type=="lobby")&&screen==ScreenMode.Lobby)
                        {remoteTeam=(Team)Mathf.Clamp(p.team,1,2);remoteSkin=Mathf.Clamp(p.skin,0,SkinFactory.Names.Length-1);remoteVote=Mathf.Clamp(p.vote,0,ArenaCount-1);remoteReady=p.ready;SendLobby();}
                        if(p.type=="input"&&screen==ScreenMode.Match)game.SetDirection(1,p.x,p.z);
                    }
                    else
                    {
                        if(p.type=="lobby") {reconnecting=false;if(screen==ScreenMode.Match)Bank();remoteName=NormalizeName(p.name,"Player");remoteTeam=(Team)Mathf.Clamp(p.team,1,2);remoteSkin=p.skin;remoteVote=p.vote;remoteReady=p.ready;duration=p.duration;if(screen!=ScreenMode.Lobby)screen=ScreenMode.Lobby;}
                        if(p.type=="roulette"&&p.votes!=null&&p.votes.Length==ArenaCount){reconnecting=false;if(screen==ScreenMode.Match)Bank();votes=p.votes;selectedArena=p.arena;rouletteStart=Time.time;spinStep=0;spinTotal=2*ArenaCount+Math.Max(0,Array.IndexOf(votes,selectedArena));nextClick=rouletteStart;rouletteLocked=false;screen=ScreenMode.Roulette;}
                        if(p.type=="state")ApplySnapshot(p);
                        if(p.type=="error")status=p.message;
                    }
                }
                catch(Exception e){status="Network data rejected: "+e.GetType().Name;}
            }
        }
        void ApplySnapshot(Packet p)
        {
            if(p.players==null||p.players.Length!=10||p.arena<0||p.arena>=ArenaCount)return;
            byte[] bytes=Convert.FromBase64String(p.owners);if(bytes.Length!=Arena.Size*Arena.Size)return;
            snapshots++;reconnecting=false;lastRxTime=Time.unscaledTime;
            bool fresh=screen!=ScreenMode.Match&&screen!=ScreenMode.Results || (int)game.Arena.Kind!=p.arena;
            if(fresh){if(game!=null)Bank();game=new GameSimulation((ArenaKind)p.arena,(Team)p.players[0].team,(Team)p.players[1].team);banked=false;}
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
            if(fresh)view.Bind(game,1);bool wasMatch=screen==ScreenMode.Match;screen=game.Phase==MatchPhase.Finished?ScreenMode.Results:ScreenMode.Match;if(!wasMatch&&screen==ScreenMode.Match){countdownEnd=Time.unscaledTime+3;lastCount=4;}if(screen==ScreenMode.Results)Bank();
        }
        void SendLobby(){Send(new Packet{type="lobby",name=playerName,team=(int)team,skin=skin,vote=vote,ready=ready,duration=duration});}
        void BeginReconnect()
        {
            if(reconnecting)return;
            reconnecting=true;reconnectUntil=Time.unscaledTime+8f;
            status="Connection lost. Reconnecting…";
            try
            {
                network?.Dispose();network=NewSession();
                if(lastOnline)network.Join(lastCode);else network.Join(lastAddress);
            }
            catch(Exception){ }
            wasConnected=false;
        }
        void OnDeepLink(string url){pendingLink=url;}
        void HandleLink(string url)
        {
            if(!JoinLink.TryParse(url,out bool linkOnline,out string target)){status="That join link did not work. Ask the host for a fresh one.";return;}
            online=linkOnline;
            if(online)joinCode=target;else address=target;
            StartLobby(false,false);
        }
        void ShareJoinLink()
        {
            string link=null;
            if(online){string code=network!=null?network.JoinCode:"";if(!string.IsNullOrEmpty(code))link=JoinLink.RelayLink(code);}
            else link=JoinLink.LanLink(LocalAddress());
            if(string.IsNullOrEmpty(link)){status="Still starting the host connection. Try again in a moment.";return;}
            GUIUtility.systemCopyBuffer=link;status="Join link copied. Send it to your partner.";
        }
        void StartLobby(bool host,bool offline)
        {
            network?.Dispose();network=null;wasConnected=false;hosting=host;practice=offline;localId=host?0:1;ready=false;remoteReady=false;remoteToken="";remoteName=offline?"CPU 1":host?"Player 2":"Player";status="";screen=ScreenMode.Lobby;reconnecting=false;
            if(!host){lastOnline=online;lastCode=joinCode;lastAddress=address;}
            if(offline){remoteReady=true;return;}
            network=NewSession();try{if(host)network.Host();else network.Join(online?joinCode:address);}catch(Exception e){status=e.Message;}
        }
        void Roulette()
        {
            votes=new int[ArenaCount];votes[0]=vote;votes[1]=practice?UnityEngine.Random.Range(0,ArenaCount):remoteVote;
            for(int i=2;i<ArenaCount;i++)votes[i]=UnityEngine.Random.Range(0,ArenaCount);
            selectedArena=votes[UnityEngine.Random.Range(0,ArenaCount)];rouletteStart=Time.time;spinStep=0;spinTotal=2*ArenaCount+Math.Max(0,Array.IndexOf(votes,selectedArena));nextClick=rouletteStart;rouletteLocked=false;screen=ScreenMode.Roulette;
            Send(new Packet{type="roulette",votes=votes,arena=selectedArena});
        }
        void StartMatch(int arena)
        {
            game=new GameSimulation((ArenaKind)arena,team,remoteTeam,Environment.TickCount,duration);game.Players[0].Skin=skin;game.Players[1].Skin=remoteSkin;
            game.Players[0].Name=hosting?playerName:remoteName;game.Players[1].Name=hosting?(practice?"CPU 1":remoteName):playerName;
            game.Event=(key,id,amount)=>{if(id==localId||key=="finish"||key=="overtime")sound.Play(key,amount);};
            view.Bind(game,localId);banked=false;accumulator=0;countdownEnd=Time.unscaledTime+3;lastCount=4;screen=ScreenMode.Match;SendUnreliable(Packet.Snapshot(game));
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
        void OnApplicationPause(bool paused){if(!paused)return;if(screen==ScreenMode.Match||screen==ScreenMode.Results)Bank();Save();}

        void Styles()
        {
            if(label!=null)return;
            UiTheme.Ensure();
            label=new GUIStyle(GUI.skin.label){fontSize=20,wordWrap=true,normal={textColor=UiTheme.Ink}};
            small=new GUIStyle(label){fontSize=15,normal={textColor=UiTheme.Muted}};heading=new GUIStyle(label){fontSize=27,fontStyle=FontStyle.Bold};title=new GUIStyle(label){fontSize=48,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){fontSize=19,padding=new RectOffset(12,12,8,8),border=new RectOffset(0,0,0,0)};
            button.normal.background=Solid(new Color(.16f,.2f,.22f));button.hover.background=Solid(new Color(.22f,.32f,.32f));button.active.background=Solid(new Color(.19f,.43f,.36f));
            btnRed=UiTheme.CardButton(button,UiTheme.BtnRed);btnRed.fontSize=20;
            btnGreen=UiTheme.CardButton(button,UiTheme.BtnGreen);btnGreen.fontSize=20;
            btnBlue=UiTheme.CardButton(button,UiTheme.BtnBlue);btnBlue.fontSize=20;
            btnGray=UiTheme.CardButton(button,UiTheme.BtnGray);btnGray.fontSize=19;
            btnOff=UiTheme.CardButton(button,UiTheme.BtnDisabled);btnOff.fontSize=19;btnOff.normal.textColor=new Color(.7f,.75f,.8f);
            panelS=new GUIStyle(GUI.skin.box){normal={background=UiTheme.Panel},padding=new RectOffset(0,0,0,0)};
            cardS=new GUIStyle(GUI.skin.box){normal={background=UiTheme.Card},padding=new RectOffset(0,0,0,0)};
            chipS=new GUIStyle(GUI.skin.box){normal={background=UiTheme.Chip},padding=new RectOffset(0,0,0,0)};
            chip=new GUIStyle(label){fontSize=15,alignment=TextAnchor.MiddleCenter,normal={textColor=UiTheme.Ink}};
            billboard=new GUIStyle(label){fontSize=110,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,normal={textColor=Color.white}};
            sliderBack=new GUIStyle(GUI.skin.horizontalSlider){fixedHeight=20};sliderBack.normal.background=UiTheme.SliderTrack;
            sliderKnob=new GUIStyle(GUI.skin.horizontalSliderThumb){fixedWidth=34,fixedHeight=34};sliderKnob.normal.background=UiTheme.SliderThumb;sliderKnob.hover.background=UiTheme.SliderThumb;sliderKnob.active.background=UiTheme.SliderThumb;
            GUI.skin.textField.fontSize=20;
        }
        GUIStyle BtnKind(Texture2D back,bool enabled){if(!enabled)return btnOff;if(back==UiTheme.BtnRed)return btnRed;if(back==UiTheme.BtnGreen)return btnGreen;if(back==UiTheme.BtnBlue)return btnBlue;return btnGray;}
        bool TBtn(Rect r,string text,Texture2D back){return GUI.Button(r,text,BtnKind(back,GUI.enabled))&&Time.unscaledTime>suppressClickUntil;}
        void Card(Rect r){if(UiTheme.Card!=null)GUI.DrawTexture(r,UiTheme.Card,ScaleMode.StretchToFill);else Fill(r,new Color(.10f,.14f,.18f,.94f));}
        void Panel(Rect r){if(UiTheme.Panel!=null)GUI.DrawTexture(r,UiTheme.Panel,ScaleMode.StretchToFill);else Fill(r,new Color(.07f,.10f,.13f,.95f));}
        void Chip(Rect r,string text){if(UiTheme.Chip!=null)GUI.DrawTexture(r,UiTheme.Chip,ScaleMode.StretchToFill);else Fill(r,new Color(.12f,.18f,.26f,.9f));GUI.Label(r,text,chip);}
        void DrawBackdrop(){if(UiTheme.Background!=null)GUI.DrawTexture(new Rect(0,0,uiWidth,uiHeight),UiTheme.Background,ScaleMode.ScaleAndCrop);else Fill(new Rect(0,0,uiWidth,uiHeight),new Color(.055f,.075f,.09f,.96f));}
        float Header(float x,float w)
        {
            bool narrow=w<600;
            float logoW=narrow?Mathf.Min(w*.52f,240):Mathf.Min(w*.66f,380),logoH=logoW/3f,y=8;
            if(UiTheme.Logo!=null)GUI.DrawTexture(new Rect(x+(w-logoW)/2,y,logoW,logoH),UiTheme.Logo,ScaleMode.ScaleToFit);
            else GUI.Label(new Rect(x,y,w,50),"PaperTrails",title);
            y+=logoH+(narrow?4:6);float pillW=narrow?120:150,ph=narrow?28:30;
            Chip(new Rect(x+(w/2-pillW-4),y,pillW,ph),team==Team.Red?"● RED":"○ RED");
            Chip(new Rect(x+w/2+4,y,pillW,ph),"5V5");
            return y+ph+(narrow?6:8);
        }
        float CenterShift(float top,float contentH,float bottomPad){return Mathf.Max(0,(uiHeight-bottomPad-top-contentH)/2);}
        void ArenaGrid(float x,float y,float w,int cols,float cellH,System.Func<int,bool> highlight,System.Func<int,Color> tint,bool showVotes,int[] counts)
        {
            float cw=w/cols;
            for(int i=0;i<ArenaCount;i++)
            {
                Rect r=new Rect(x+(i%cols)*cw,y+(i/cols)*cellH,cw-6,cellH-6);
                Card(r);
                if(highlight!=null&&highlight(i))Fill(r,new Color(.15f,.7f,.4f,.28f));else Fill(r,new Color(1,1,1,.04f));
                float iconH=cellH-44;if(iconH<24)iconH=24;
                GUI.DrawTexture(new Rect(r.x+4,r.y+3,r.width-8,iconH),previews[i],ScaleMode.ScaleToFit);
                string name=ArenaName(i);if(showVotes&&counts!=null)name+=$"  ({counts[i]})";
                var st=new GUIStyle(small){fontSize=w<600?10:13,alignment=TextAnchor.UpperCenter,wordWrap=true,normal={textColor=highlight!=null&&highlight(i)?Color.white:UiTheme.Ink}};
                GUI.Label(new Rect(r.x+2,r.y+iconH+4,r.width-4,cellH-iconH-6),name,st);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none)&&Time.unscaledTime>suppressClickUntil){vote=i;ready=false;SendLobby();}
            }
        }
        void SkinRow(float x,float y,float w)
        {
            var order=new List<int>(unlocked);order.Sort();
            int at=Math.Max(0,order.IndexOf(skin));
            if(TBtn(new Rect(x,y,46,52),"◀",UiTheme.BtnGray)){skin=order[(at-1+order.Count)%order.Count];Save();ready=false;SendLobby();}
            if(TBtn(new Rect(x+w-46,y,46,52),"▶",UiTheme.BtnGray)){skin=order[(at+1)%order.Count];Save();ready=false;SendLobby();}
            GUI.DrawTexture(new Rect(x+52,y+8,36,36),studio.Icon(skin),ScaleMode.ScaleToFit);
            GUI.Label(new Rect(x+92,y,w-92-50,52),ShortName(SkinFactory.Names[skin]),new GUIStyle(label){fontSize=15,alignment=TextAnchor.MiddleLeft});
        }
        void ThemedMinutes(Rect r)
        {
            duration=Mathf.Round(GUI.HorizontalSlider(r,duration,60,600,sliderBack,sliderKnob)/60)*60;
        }
        void OnGUI()
        {
            Styles();Rect safe=UnityEngine.Screen.safeArea;float scale=Mathf.Min(safe.height/800f,safe.width/420f);uiScale=scale;uiWidth=safe.width/scale;uiHeight=safe.height/scale;
            GUI.matrix=Matrix4x4.TRS(new Vector3(safe.x,UnityEngine.Screen.height-safe.yMax,0),Quaternion.identity,new Vector3(scale,scale,1));
            if(screen==ScreenMode.Match){Hud();return;}
            DrawBackdrop();
            float w=Mathf.Min(uiWidth-40,960),x=(uiWidth-w)/2;
            switch(screen)
            {
                case ScreenMode.Menu:
                {
                    float y0=Header(x,w);
                    if(w>=600)GUI.DrawTexture(new Rect(x+w*.55f,y0+40,w*.4f,w*.4f),previews[0],ScaleMode.ScaleToFit);
                    float bw=w<600?w:Mathf.Min(w*.48f,360);bool nm=w<600;float bh=nm?48:54;
                    y0+=CenterShift(y0,nm?410:440,60);
                    Panel(new Rect(x-10,y0-6,bw+20,nm?110:116));
                    GUI.Label(new Rect(x,y0,bw,26),"Player name",small);
                    string typedName=GUI.TextField(new Rect(x,y0+28,bw,42),playerName,16);if(typedName!=playerName){playerName=typedName;Save();}
                    if(TBtn(new Rect(x,y0+76,bw/2-4,40),online?"Online ✓":"Online",online?UiTheme.BtnBlue:UiTheme.BtnGray))online=true;
                    if(TBtn(new Rect(x+bw/2+4,y0+76,bw/2-4,40),online?"LAN":"LAN ✓",!online?UiTheme.BtnBlue:UiTheme.BtnGray))online=false;
                    float my=y0+(nm?116:122);
                    if(TBtn(new Rect(x,my,bw,bh),"Host game",UiTheme.BtnGreen))StartLobby(true,false);my+=bh+8;
                    float joinH=26+42+6+bh;
                    Panel(new Rect(x-10,my-6,bw+20,joinH+12));
                    GUI.Label(new Rect(x,my,bw,26),online?"Join code":"Host IP address",small);
                    if(online)joinCode=GUI.TextField(new Rect(x,my+28,bw,42),joinCode).ToUpperInvariant();else address=GUI.TextField(new Rect(x,my+28,bw,42),address);
                    if(TBtn(new Rect(x,my+76,bw,bh),"Join game",UiTheme.BtnBlue))StartLobby(false,false);my+=joinH+12;
                    if(TBtn(new Rect(x,my,bw,bh),"Practice with CPUs",UiTheme.BtnRed))StartLobby(true,true);my+=bh+8;
                    if(TBtn(new Rect(x,my,bw,bh),"Collection  /  "+wallet+" coins",UiTheme.BtnGray))screen=ScreenMode.Collection;
                    GUI.Label(new Rect(x,uiHeight-50,w,32),online?"Online multiplayer":"LAN  /  "+LocalAddress(),small);break;
                }
                case ScreenMode.Lobby: LobbyUI(x,w);break;
                case ScreenMode.Roulette:
                {
                    float ry=Header(x,w);
                    bool narrowR=w<600;
                    ry+=CenterShift(ry,narrowR?486:322,40);
                    GUI.Label(new Rect(x,ry,w,40),"Arena roulette",heading);
                    float rt=Time.time-rouletteStart;
                    bool locked=rt>=3;
                    int index=spinStep%ArenaCount;
                    Chip(new Rect(x+(w-360)/2,ry+44,360,32),rt>=3?ArenaName(selectedArena)+"  •  Starting…":"Spinning… good luck!");
                    int cols=narrowR?6:11;float cellH=narrowR?100:118;float gy=ry+86;float cw=w/cols;
                    float pulse=rt>=3?.5f+.3f*Mathf.Sin(Time.time*12):0;
                    string nm0=hosting?playerName:remoteName,nm1=hosting?remoteName:playerName;
                    for(int i=0;i<ArenaCount;i++)
                    {
                        Rect r=new Rect(x+(i%cols)*cw,gy+(i/cols)*cellH,cw-6,cellH-6);
                        Card(r);
                        bool won=locked&&votes[i]==selectedArena;
                        if(won)Fill(r,new Color(.15f,.78f,.42f,.35f+pulse*.4f));
                        else if(!locked&&i==index)Fill(r,new Color(.2f,.5f,.45f,.55f));
                        float ih=narrowR?40:64;
                        GUI.DrawTexture(new Rect(r.x+5,r.y+4,r.width-10,ih),previews[votes[i]],ScaleMode.ScaleToFit);
                        GUI.Label(new Rect(r.x+2,r.y+ih+5,r.width-4,36),ArenaName(votes[i]),new GUIStyle(small){fontSize=narrowR?10:13,alignment=TextAnchor.UpperCenter,normal={textColor=won?Color.white:UiTheme.Ink}});
                        if(i<2)GUI.Label(new Rect(r.x+2,r.y+ih+41,r.width-4,14),ShortName(i==0?nm0:nm1),new GUIStyle(small){fontSize=narrowR?9:10,alignment=TextAnchor.UpperCenter,normal={textColor=UiTheme.Gold}});
                    }
                    break;
                }
                case ScreenMode.Results:
                {
                    float ry2=Header(x,w);
                    ry2+=CenterShift(ry2,442,40);
                    float cw2=Mathf.Min(w,560),cx2=x+(w-cw2)/2;
                    Panel(new Rect(cx2-14,ry2-6,cw2+28,436));
                    GUI.contentColor=GameView.TeamColor(game.Winner);
                    GUI.Label(new Rect(cx2,ry2,cw2,60),game.Winner.ToString().ToUpper()+" WINS",new GUIStyle(title){alignment=TextAnchor.MiddleCenter});
                    GUI.contentColor=Color.white;
                    Chip(new Rect(cx2+20,ry2+66,cw2/2-30,36),$"RED  {Percent(game.RedCount):0.0}%");
                    Chip(new Rect(cx2+cw2/2+10,ry2+66,cw2/2-30,36),$"BLUE  {Percent(game.BlueCount):0.0}%");
                    Player rp=game.Players[localId];
                    GUI.Label(new Rect(cx2+20,ry2+112,cw2-40,130),$"Territory captured    {rp.Captured} cells\nLargest capture        {rp.Largest} cells\nTrail cuts   {rp.Cuts}       Deaths   {rp.Deaths}\nCoins collected    +{rp.Coins}",label);
                    if(hosting&&TBtn(new Rect(cx2+20,ry2+252,250,55),"Return to lobby",UiTheme.BtnGreen))ReturnLobby();
                    if(!hosting)GUI.Label(new Rect(cx2+20,ry2+252,cw2-40,45),"Waiting for host to return to lobby",label);
                    if(TBtn(new Rect(cx2+20,ry2+318,cw2-40,55),"Main menu",UiTheme.BtnGray)){network?.Dispose();network=null;screen=ScreenMode.Menu;}
                    break;
                }
                case ScreenMode.Collection: CollectionUI(x,w);break;
            }
        }
        void LobbyUI(float x,float w)
        {
            if(w<600){PortraitLobby(x,w);return;}
            float y=Header(x,w);
            y+=CenterShift(y,521,30);
            GUI.Label(new Rect(x,y,w,34),practice?"Practice lobby":hosting?"Host  /  "+SessionAddress():"Guest  /  "+(online?joinCode:address),heading);y+=36;
            GUI.Label(new Rect(x,y,w,30),status!=""?status:practice?"Second human slot uses CPU control":network?.Status,small);y+=34;
            if(TBtn(new Rect(x,y,w*.23f,50),team==Team.Red?"Red  ✓":"Red",UiTheme.BtnRed)){team=Team.Red;ready=false;SendLobby();}
            if(TBtn(new Rect(x+w*.25f,y,w*.23f,50),team==Team.Blue?"Blue  ✓":"Blue",UiTheme.BtnBlue)){team=Team.Blue;ready=false;SendLobby();}
            int red=(team==Team.Red?1:0)+(remoteTeam==Team.Red?1:0);
            Chip(new Rect(x+w*.52f,y-4,w*.48f,58),$"Red {red} human + {5-red} CPU   •   Blue {2-red} human + {3+red} CPU");
            y+=58;
            SkinRow(x,y,w*.48f);
            if(hosting){GUI.Label(new Rect(x+w*.52f,y,w*.52f,26),"Match length: "+(duration/60).ToString("0")+" minutes",small);ThemedMinutes(new Rect(x+w*.52f,y+24,w*.44f,22));}
            y+=54;
            GUI.Label(new Rect(x,y,w*.5f,30),"Your arena vote",label);
            Chip(new Rect(x+w*.62f,y,w*.38f,28),"Pick your favorite arena!");
            y+=30;
            ArenaGrid(x,y,w,11,95,i=>i==vote,null,false,null);
            y+=2*95+8;
            if(TBtn(new Rect(x,y,w*.42f,52),ready?"Ready ✓":"Ready",ready?UiTheme.BtnGreen:UiTheme.BtnGray)){ready=!ready;SendLobby();}
            Chip(new Rect(x+w*.45f,y+10,w*.2f,32),remoteReady?"Partner ready":"Partner not ready");
            GUI.enabled=ready&&(practice||network!=null&&network.Connected&&remoteReady);
            if(hosting&&TBtn(new Rect(x+w*.68f,y,w*.32f,52),"Start match",UiTheme.BtnGreen))Roulette();GUI.enabled=true;
            y+=58;
            if(TBtn(new Rect(x,y,160,44),"Back",UiTheme.BtnGray)){network?.Dispose();network=null;screen=ScreenMode.Menu;}
            if(hosting&&!practice&&TBtn(new Rect(x+176,y,230,44),"Share join link",UiTheme.BtnBlue))ShareJoinLink();
            if(!hosting&&network!=null&&!network.Connected&&TBtn(new Rect(x+176,y,190,44),"Reconnect",UiTheme.BtnBlue)){network.Dispose();network=NewSession();network.Join(online?joinCode:address);wasConnected=false;}
        }
        void PortraitLobby(float x,float w)
        {
            float y=Header(x,w);
            y+=CenterShift(y,665,20);
            GUI.Label(new Rect(x,y,w,32),practice?"Practice lobby":hosting?"Host lobby":"Join lobby",heading);y+=34;
            GUI.Label(new Rect(x,y,w,28),status!=""?status:practice?"Second slot: CPU":hosting?SessionAddress()+" / "+network?.Status:network?.Status,small);y+=30;
            if(TBtn(new Rect(x,y,w/2-5,46),team==Team.Red?"Red ✓":"Red",UiTheme.BtnRed)){team=Team.Red;ready=false;SendLobby();}
            if(TBtn(new Rect(x+w/2+5,y,w/2-5,46),team==Team.Blue?"Blue ✓":"Blue",UiTheme.BtnBlue)){team=Team.Blue;ready=false;SendLobby();}
            y+=52;
            int red=(team==Team.Red?1:0)+(remoteTeam==Team.Red?1:0);
            SkinRow(x,y,w*.55f);
            Chip(new Rect(x+w*.58f,y,w*.42f,52),$"Red {red}+{5-red} CPU\nBlue {2-red}+{3+red} CPU");
            y+=58;
            GUI.Label(new Rect(x,y,w*.55f,24),"Match: "+(duration/60).ToString("0")+" minutes",small);
            if(hosting)ThemedMinutes(new Rect(x+w*.55f,y+2,w*.45f,20));
            y+=28;
            GUI.Label(new Rect(x,y,w,28),"Your arena vote",label);y+=30;
            ArenaGrid(x,y,w,6,70,i=>i==vote,null,false,null);
            y+=4*70+8;
            if(TBtn(new Rect(x,y,w*.48f,46),ready?"Ready ✓":"Ready",ready?UiTheme.BtnGreen:UiTheme.BtnGray)){ready=!ready;SendLobby();}
            GUI.enabled=ready&&(practice||network!=null&&network.Connected&&remoteReady);
            if(hosting&&TBtn(new Rect(x+w*.52f,y,w*.48f,46),"Start match",UiTheme.BtnGreen))Roulette();else if(!hosting)Chip(new Rect(x+w*.52f,y+8,w*.48f,30),remoteReady?"Partner ready":"Waiting…");
            GUI.enabled=true;
            y+=52;
            GUI.Label(new Rect(x,y,w,24),remoteReady?"Partner ready ✓":"Partner not ready",new GUIStyle(small){alignment=TextAnchor.MiddleCenter});
            y+=26;
            if(TBtn(new Rect(x,y,w*.48f,42),"Back",UiTheme.BtnGray)){network?.Dispose();network=null;screen=ScreenMode.Menu;}
            if(hosting&&!practice&&TBtn(new Rect(x+w*.52f,y,w*.48f,42),"Share link",UiTheme.BtnBlue))ShareJoinLink();
            if(!hosting&&network!=null&&!network.Connected&&TBtn(new Rect(x+w*.52f,y,w*.48f,42),"Reconnect",UiTheme.BtnBlue)){network.Dispose();network=NewSession();network.Join(online?joinCode:address);wasConnected=false;}
        }
        void CollectionUI(float x,float w)
        {
            bool narrow=w<600;
            if(string.IsNullOrEmpty(recoveryCode))recoveryCode=CreateRecoveryCode();
            float cy=Header(x,w);
            GUI.Label(new Rect(x,cy,narrow?w*.62f:w*.6f,40),"Capsule Collection",new GUIStyle(heading){fontSize=narrow?20:27});
            Chip(new Rect(x+w*.64f,cy+4,w*.36f,32),wallet+" coins");
            cy+=48;
            float panelH=narrow?196:200;
            Panel(new Rect(x-8,cy-6,w+16,panelH+12));
            GUI.DrawTexture(narrow?new Rect(x,cy,w*.44f,160):new Rect(x+w*.62f,cy-4,w*.36f,190),studio.Machine(Time.time-unlockTime),ScaleMode.ScaleToFit);
            GUI.enabled=wallet>=Cost&&unlocked.Count<SkinFactory.Names.Length&&Time.time-unlockTime>=2;
            if(TBtn(narrow?new Rect(x+w*.47f,cy+10,w*.53f,60):new Rect(x,cy+20,Mathf.Min(w*.5f,380),55),narrow?$"Turn / {Cost} coins":$"Turn capsule  /  {Cost} coins",UiTheme.BtnRed))
            {
                var locked=new List<int>();for(int i=0;i<SkinFactory.Names.Length;i++)if(!unlocked.Contains(i))locked.Add(i);
                int choice=locked[UnityEngine.Random.Range(0,locked.Count)];wallet-=Cost;unlocked.Add(choice);skin=choice;reveal=SkinFactory.Names[choice]+" unlocked";unlockTime=Time.time;Save();sound.Play("unlock");
            }
            GUI.enabled=true;
            GUI.Label(narrow?new Rect(x+w*.47f,cy+78,w*.53f,110):new Rect(x,cy+85,Mathf.Min(w*.5f,380),100),Time.time-unlockTime>=2?reveal:"Opening capsule...",new GUIStyle(label){fontSize=narrow?17:19,wordWrap=true});
            cy+=panelH+14;
            Panel(new Rect(x-8,cy-6,w+16,narrow?150:150));
            GUI.Label(new Rect(x,cy,w*.68f,22),"Recovery code",small);
            GUI.Label(new Rect(x,cy+20,w*.68f,34),recoveryCode,new GUIStyle(small){fontSize=narrow?10:12,wordWrap=false});
            if(TBtn(new Rect(x+w*.70f,cy+16,w*.30f,36),"Copy",UiTheme.BtnBlue)){GUIUtility.systemCopyBuffer=recoveryCode;recoveryCopied=true;}
            GUI.Label(new Rect(x,cy+56,w*.68f,22),recoveryCopied?"Copied to clipboard":"Keep this code somewhere safe",small);
            restoreCode=GUI.TextField(new Rect(x,cy+80,w*.68f,38),restoreCode);
            if(TBtn(new Rect(x+w*.70f,cy+80,w*.30f,38),"Restore",UiTheme.BtnGray))if(!RestoreRecoveryCode())reveal="Invalid recovery code";
            cy+=narrow?160:160;
            Panel(new Rect(x-8,cy-6,w+16,76));
            GUI.DrawTexture(new Rect(x+2,cy-2,64,64),studio.BigIcon(skin),ScaleMode.ScaleToFit);
            Chip(new Rect(x+74,cy,w*.3f,24),"NOW USING");
            GUI.Label(new Rect(x+74,cy+26,w-80,40),SkinFactory.Names[skin],new GUIStyle(heading){fontSize=narrow?20:24});
            cy+=82;
            Rect area=new Rect(x,cy,w,uiHeight-cy-70);int columns=narrow?2:3;float cellH=narrow?60:64;
            collectionScroll=GUI.BeginScrollView(area,collectionScroll,new Rect(0,0,w-25,Mathf.Ceil(SkinFactory.Names.Length/(float)columns)*cellH));
            for(int i=0;i<SkinFactory.Names.Length;i++)
            {GUI.enabled=unlocked.Contains(i);float cx=i%columns*(w-25)/columns,cy2=i/columns*cellH,cellWidth=(w-25)/columns-8;var cell=new Rect(cx,cy2,cellWidth,cellH-8);Card(cell);if(i==skin)Fill(cell,new Color(.95f,.73f,.16f,.22f));if(GUI.Button(cell,GUIContent.none,GUIStyle.none)&&Time.unscaledTime>suppressClickUntil){skin=i;Save();}GUI.DrawTexture(new Rect(cx+4,cy2+4,48,48),studio.Icon(i),ScaleMode.ScaleToFit);GUI.Label(new Rect(cx+56,cy2+10,cellWidth-58,44),(skin==i?"✓ ":"")+SkinFactory.Names[i],new GUIStyle(small){fontSize=narrow?13:16,wordWrap=true,normal={textColor=unlocked.Contains(i)?UiTheme.Ink:new Color(.45f,.5f,.55f)}});}GUI.enabled=true;GUI.EndScrollView();
            if(TBtn(new Rect(x,uiHeight-60,200,45),"Back",UiTheme.BtnGray))screen=ScreenMode.Menu;
        }
        void Hud()
        {
            bool portrait=uiWidth<600;float barH=portrait?116:82;
            Panel(new Rect(-20,-20,uiWidth+40,barH+20));
            var scoreL=new GUIStyle(heading){fontSize=portrait?20:26,normal={textColor=GameView.Red}};
            var scoreR=new GUIStyle(heading){fontSize=portrait?20:26,alignment=TextAnchor.MiddleRight,normal={textColor=GameView.Blue}};
            GUI.Label(new Rect(20,portrait?10:8,uiWidth*.32f,40),$"RED  {Percent(game.RedCount):0.0}%",scoreL);
            GUI.Label(new Rect(uiWidth-uiWidth*.32f-20,portrait?10:8,uiWidth*.32f,40),$"BLUE  {Percent(game.BlueCount):0.0}%",scoreR);
            string time=game.Phase==MatchPhase.Overtime?"OVERTIME":TimeSpan.FromSeconds(Mathf.Ceil(game.Remaining)).ToString(@"m\:ss");
            Chip(new Rect(uiWidth/2-95,portrait?54:16,190,46),"");
            GUI.Label(new Rect(uiWidth/2-95,portrait?54:16,190,46),time,new GUIStyle(heading){fontSize=24,alignment=TextAnchor.MiddleCenter});
            Chip(new Rect(12,portrait?122:88,150,36),game.Players[localId].Coins+" coins");
            float size=Mathf.Min(190,uiWidth*.28f);Rect map=new Rect(18,uiHeight-size-18,size,size);
            Panel(new Rect(map.x-6,map.y-6,map.width+12,map.height+12));GUI.DrawTexture(map,view.Map);
            foreach(Player p in game.Players)
            {
                if(!p.Alive)continue;float px=map.x+p.X/Arena.Size*size,py=map.yMax-p.Z/Arena.Size*size;
                Fill(new Rect(px-3,py-3,6,6),GameView.TeamColor(p.Team));
                if(p.Human){GUI.DrawTexture(new Rect(px-12,py-12,24,24),studio.Icon(p.Skin),ScaleMode.ScaleToFit);GUI.Label(new Rect(px-40,py-25,100,22),NormalizeName(p.Name,p.Id==localId?"YOU":"P"+(p.Id+1)),new GUIStyle(small){fontSize=11,normal={textColor=Color.white}});}
            }
            for(int t=0;t<2;t++){int h=game.Arena.Hubs[t];GUI.Label(new Rect(map.x+(h%Arena.Size)/80f*size-5,map.yMax-(h/Arena.Size)/80f*size-10,20,20),"H",small);}
            var centered=new GUIStyle(heading){alignment=TextAnchor.MiddleCenter};
            Player me=game.Players[localId];if(!me.Alive){Panel(new Rect(uiWidth/2-170,uiHeight/2-50,340,100));GUI.Label(new Rect(uiWidth/2-160,uiHeight/2-25,320,60),$"Respawning  {me.Respawn:0.0}",centered);}
            float remain=countdownEnd-Time.unscaledTime;
            if(remain>0){Fill(new Rect(0,0,uiWidth,uiHeight),new Color(0,0,0,.45f));GUI.Label(new Rect(uiWidth/2-200,uiHeight/2-140,400,150),Mathf.CeilToInt(remain).ToString(),billboard);GUI.Label(new Rect(uiWidth/2-200,uiHeight/2+10,400,50),"Get Ready!",centered);}
            else if(remain>-0.5f&&lastCount==0&&countdownEnd>0){Fill(new Rect(0,0,uiWidth,uiHeight),new Color(0,0,0,.25f));GUI.Label(new Rect(uiWidth/2-200,uiHeight/2-140,400,150),"GO!",new GUIStyle(billboard){normal={textColor=UiTheme.Mint}});}
            if(reconnecting){Panel(new Rect(uiWidth/2-190,uiHeight/2-120,380,160));GUI.Label(new Rect(uiWidth/2-180,uiHeight/2-105,360,50),"Reconnecting…",centered);if(TBtn(new Rect(uiWidth/2-140,uiHeight/2-40,280,50),"Back to lobby",UiTheme.BtnGray)){reconnecting=false;network?.Dispose();network=null;screen=ScreenMode.Menu;status="Disconnected.";}}
        }
        float Percent(int count)=>100f*count/game.Arena.Claimable;
        static string ArenaName(int id)=>id>=0&&id<ArenaNames.Length?ArenaNames[id]:((ArenaKind)id).ToString();
        static string ShortName(string s){s=(s??"").Trim();return s.Length>9?s.Substring(0,9)+"…":s;}
        bool Btn(Rect r,string text)=>GUI.Button(r,text,button)&&Time.unscaledTime>suppressClickUntil;
        IGameSession NewSession()=>online?(IGameSession)new RelaySession():new LanSession();
        string SessionAddress()=>online?"Code "+network?.JoinCode:LocalAddress();
        static Texture2D Solid(Color color){var t=new Texture2D(1,1);t.SetPixel(0,0,color);t.Apply();return t;}
        static void Fill(Rect r,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        static string LocalAddress(){try{foreach(var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)if(ip.AddressFamily==AddressFamily.InterNetwork)return ip.ToString();}catch(Exception){}return "127.0.0.1";}
    }
}
