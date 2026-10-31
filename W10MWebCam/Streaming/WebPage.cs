namespace W10MWebCam.Streaming
{
    /// <summary>
    /// The settings page (http://127.0.0.1:8020/settings over USB, http://PHONE-IP:5001/settings over Wi-Fi; changes
    /// after logging in), in the green theme shared with W10MSecurityCam and W10MDashcam. Plain HTML and a little
    /// script; it polls /settings/state every 2 s and posts changes to /settings/set.
    /// The page source avoids double quotes so it can live in a C# verbatim string.
    /// </summary>
    internal static class WebPage
    {
        public const string Html = @"<!doctype html>
<html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>W10MWebCam</title>
<style>
body{font-family:'Segoe UI',system-ui,sans-serif;background:#000;color:#d6e8d9;margin:0;padding:16px;max-width:520px}
h1{font-size:19px;margin:0 0 4px;display:flex;justify-content:space-between;align-items:baseline;color:#e4f5e7;letter-spacing:.3px}
#ver{font-size:12px;font-weight:bold;color:#39ff14;text-shadow:0 0 6px rgba(57,255,20,.45)}
.sub{color:#7f9a86;font-size:12px;margin:4px 0;word-break:break-word}
.row{margin:12px 0}.head{display:flex;justify-content:space-between;align-items:center;font-size:14px}
input[type=range]{width:100%;accent-color:#3fbf5f}input[type=range]:disabled{opacity:.35}input[type=checkbox]{accent-color:#3fbf5f}
select{background:#0d140f;color:#d6e8d9;border:1px solid #1f3a26;border-radius:4px;padding:6px;font-size:14px}
button{background:#123320;color:#cfffd8;border:1px solid #2e7d46;border-radius:6px;padding:8px 12px;font-size:14px;margin-top:6px;cursor:pointer}
button:hover:not(:disabled){background:#18452b;box-shadow:0 0 8px rgba(63,191,95,.35)}
button:disabled,select:disabled,input:disabled{opacity:.35;cursor:default}
label{font-size:13px;color:#b9cfbd}#msg{color:#9cf0b0;font-size:12px;min-height:16px;margin-top:10px}
h2{font-size:13px;color:#9cf0b0;background:#0b1a10;text-transform:uppercase;letter-spacing:1px;border:1px solid #1f3a26;border-left:3px solid #3fbf5f;border-radius:4px;padding:8px 10px;margin:18px 0 6px;display:flex;justify-content:space-between;align-items:center;cursor:pointer;user-select:none}
h2 .ar{font-size:16px;color:#3fbf5f;padding:0 4px}
.card{background:linear-gradient(180deg,#07140b,#030805);border:1px solid #1f3a26;border-radius:8px;padding:10px 12px;margin-top:6px}
.top{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.dot{width:10px;height:10px;border-radius:50%;background:#555;flex:none}
.dot.on{background:#39ff14;box-shadow:0 0 8px #39ff14;animation:pulse 2s ease-in-out infinite}
.dot.hot{background:#ff5555;box-shadow:0 0 8px #ff5555;animation:pulse 1s ease-in-out infinite}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.35}}
.state{font-weight:600;letter-spacing:1px;font-size:13px;text-transform:uppercase;color:#9cf0b0}
.pill{font-size:11px;color:#9cf0b0;border:1px solid #2e7d46;border-radius:10px;padding:1px 8px;background:#0b1a10}
.pill.r{margin-left:auto}
.big{font-family:Consolas,'Cascadia Mono',monospace;font-size:22px;color:#e4f5e7;margin:8px 0 2px}
.big small{font-size:12px;color:#7f9a86;margin:0 10px 0 3px}
.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:6px;margin-top:8px}
.tile{background:#0b1a10;border:1px solid #16301e;border-radius:6px;padding:6px 8px;min-width:0}
.tile b{display:block;font-size:10px;letter-spacing:1px;text-transform:uppercase;color:#6f8f77;font-weight:600}
.tile span{font-family:Consolas,'Cascadia Mono',monospace;font-size:13px;color:#d6e8d9;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;display:block}
.tile.warn span{color:#ff8a8a}
.meter{height:6px;background:#06100a;border:1px solid #1f3a26;border-radius:3px;overflow:hidden;margin-top:4px}
.meter>div{height:100%;background:linear-gradient(90deg,#2fa84f,#39ff14);border-radius:2px}
.meter.mid>div{background:linear-gradient(90deg,#b8a02f,#e8d040)}.meter.low>div{background:linear-gradient(90deg,#a83232,#ff5555)}
.batt{display:flex;align-items:center;gap:2px;margin-top:4px}.batt .meter{flex:1;height:10px;margin:0;border-radius:3px}
.batt .cap{width:3px;height:5px;background:#2e7d46;border-radius:0 2px 2px 0}
.bars{display:flex;align-items:flex-end;gap:3px;height:14px;margin-top:4px}.bars i{display:block;width:6px;background:#16301e;border-radius:1px;margin:0}
.bars i.on{background:#39ff14;box-shadow:0 0 4px rgba(57,255,20,.5)}.bars.weak i.on{background:#e8d040;box-shadow:none}
.tile.warn .meter>div,.tile.warn .bars i.on{background:#ff5555;box-shadow:none}
.tile[data-tip]{cursor:help}
.notice{display:flex;align-items:center;gap:8px;margin-top:8px;padding:7px 10px;border:1px solid #7d6e2e;border-left:3px solid #e8d040;border-radius:6px;background:#1a1808;color:#e8d8a0;font-size:12px}
.notice .ni{flex:none;width:16px;height:16px;line-height:16px;text-align:center;border-radius:50%;background:#e8d040;color:#1a1808;font-weight:bold;font-size:11px}
.notice code,.addr code{font-family:Consolas,'Cascadia Mono',monospace}
.notice.bad{border-color:#7d2e2e;border-left-color:#ff5555;background:#1a0808;color:#ffb0b0}.notice.bad .ni{background:#ff5555;color:#1a0808}
.notice button{margin:0 0 0 auto;padding:4px 10px;font-size:12px}
.badge{display:inline-flex;align-items:center;gap:6px;margin-top:5px;padding:2px 9px 2px 7px;border-radius:11px;font-size:12px;font-weight:600;letter-spacing:.3px;border:1px solid #2e7d46;color:#39ff14;background:#0f2a17}
.badge::before{content:'';width:8px;height:8px;border-radius:50%;background:currentColor;box-shadow:0 0 6px currentColor}
.badge.mid{color:#e8d040;border-color:#7d6e2e;background:#2a2610}.badge.bad{color:#ff5555;border-color:#7d2e2e;background:#2a1010}
.badge.bad::before{animation:pulse 1s ease-in-out infinite}
.tip{display:inline-block;width:15px;height:15px;line-height:15px;text-align:center;border-radius:50%;border:1px solid #2e7d46;color:#3fbf5f;font-size:10px;font-style:normal;font-weight:bold;cursor:help;margin-left:6px;vertical-align:middle}
#tipbox{position:fixed;display:none;max-width:260px;background:#0b1a10;border:1px solid #2e7d46;color:#d6e8d9;padding:8px 10px;border-radius:6px;font-size:12px;line-height:1.45;z-index:50;box-shadow:0 6px 16px rgba(0,0,0,.7);pointer-events:none}
.vrow{display:flex;gap:6px;align-items:center;flex-wrap:wrap}.vrow select{flex:1 1 30%;min-width:0}
.btnrow{display:flex;gap:6px;flex-wrap:wrap;align-items:center}
.grp{background:#0b1a10;border:1px solid #16301e;border-radius:6px;padding:6px 10px;margin:8px 0}.grp>b{display:block;font-size:11px;letter-spacing:1px;text-transform:uppercase;color:#9cf0b0;margin-bottom:2px}
.grp .addr{margin:4px 0}.grp .addr code{padding:4px 8px}.links{margin:2px 0 6px 22px;font-family:Consolas,'Cascadia Mono',monospace;font-size:12px;color:#7f9a86;line-height:1.6}.links a{color:#3fbf5f}.links.off{opacity:.4}
.st{display:inline-block;font-size:10px;font-weight:700;letter-spacing:1px;border-radius:9px;padding:0 7px;margin-left:8px;border:1px solid #2e7d46;color:#39ff14;background:#0f2a17;vertical-align:1px}.st.off{color:#7f9a86;border-color:#1f3a26;background:#0b1a10}
.hint{font-size:11px;color:#7f9a86;margin-left:6px}
input.tx{background:#0d140f;color:#d6e8d9;border:1px solid #1f3a26;border-radius:4px;padding:6px;font-size:14px;width:46%;margin:2px 0}
.addr{margin:8px 0}.addr b{display:block;font-size:11px;letter-spacing:1px;text-transform:uppercase;color:#6f8f77}
.addr code{display:block;font-size:13px;color:#e4f5e7;background:#030805;border:1px solid #16301e;border-radius:4px;padding:6px 8px;margin-top:3px;word-break:break-all;user-select:all}
#ro{display:none}
</style></head><body>
<h1><span>W10MWebCam</span><span id='ver'></span></h1>
<div class='notice' id='ro'><span class='ni'>i</span><span id='rotext'>View only.</span><button id='loginb' style='display:none'>Log in</button></div>

<h2>Status</h2>
<div class='row'><div id='stat'>loading...</div><div id='warn'></div></div>

<h2>Connect</h2>
<div class='row'><div id='connect'></div>
<div class='sub'>How to open the video in OBS, VLC or ffplay: see <a href='#viewer' id='toviewer' style='color:#3fbf5f'>Viewer setup</a> below.</div></div>

<h2>Video</h2>
<div class='row'>
<div class='vrow' style='margin-bottom:6px'><label>Camera <select id='lens'><option value='rear'>Rear</option><option value='front'>Front</option></select></label><i class='tip' data-tip='Rear: the main camera, up to 4K30. Front: the selfie camera (1080p at most on the Lumia 950). Switching restarts the camera and the stream.'>i</i></div>
<div class='vrow'><select id='format' data-tip='Resolution and frame rate'></select><select id='bitrate' data-tip='Bitrate. Auto is about 37 Mbps at 4K30 and 9 Mbps at 1080p30; USB carries 60+ Mbps.'></select></div>
<div class='vrow' style='margin-top:6px'><label>Keyframe every <select id='keyframe'></select></label><i class='tip' data-tip='A full picture this often. Shorter: OBS joins and recovers faster, at slightly more data. It does not change the delay.'>i</i></div>
<div class='btnrow' style='margin-top:6px'><button id='stream'>Start stream</button></div>
<div class='sub'>Changing these restarts the stream for a moment; OBS reconnects by itself.</div>
<div class='row'><label><input type='checkbox' id='autostart'> Start streaming when the app opens</label></div>
<div class='row'><label><input type='checkbox' id='preview'> Preview on the phone</label><i class='tip' data-tip='Shown when the screen is not blacked out. Changes apply when the stream is stopped or restarted: switching the preview during a 4K stream can stall it.'>i</i></div>
</div>

<h2>Camera</h2>
<div class='sub' id='camnow'></div>
<div class='row'><label><input type='checkbox' id='camauto'> Automatic focus, exposure and white balance</label></div>
<div class='row'><div class='head'><span id='fl'>Focus</span><label><input type='checkbox' id='fa'> Auto</label></div>
<input type='range' id='fs' min='0' max='100' step='1'>
<div class='btnrow'><button id='fonce'>Focus once and lock</button></div></div>
<div class='row'><div class='head'><span id='el'>Exposure</span><label><input type='checkbox' id='ea'> Auto</label></div>
<input type='range' id='es' min='0' max='100' step='0.5'></div>
<div class='row'><div class='head'><span id='wl'>White balance</span><label><input type='checkbox' id='wa'> Auto</label></div>
<input type='range' id='ws' min='2000' max='8000' step='100'></div>
<div class='row'><div class='head'><span>Flashlight<i class='tip' data-tip='Only while streaming. Switches off by itself after 30 seconds: the LED heats the phone quickly and this phone cannot dim it.'>i</i></span><label><input type='checkbox' id='torch'> On</label></div>
<div class='sub' id='torchnote'></div></div>
<div class='row'><label><input type='checkbox' id='ois'> Stabilization off (fixed mount)</label><i class='tip' data-tip='For a phone on a tripod or mount: less processing and heat. Changing it restarts the stream once.'>i</i></div>
<div class='row'><label><input type='checkbox' id='sf'> Hold focus once it has settled</label><i class='tip' data-tip='Parks the lens after autofocus settles, so it stops hunting. Not for a subject that moves toward or away from the camera.'>i</i></div>

<h2>Power</h2>
<div class='row'><label><input type='checkbox' id='powersave'> Black screen while streaming</label><i class='tip' data-tip='Black OLED screen at minimum brightness: saves battery and lets the phone charge while it streams 4K.'>i</i></div>
<div class='row'><label><input type='checkbox' id='heatauto'> Lower to 1080p if the phone overheats</label><i class='tip' data-tip='When charging pauses on a good charger for 20 s the battery is hot. Off: only a warning. On: the stream drops to 1080p / 6 Mbps until charging has resumed (at least 3 minutes); your own settings stay as they are.'>i</i></div>
<div class='row'><label>When unplugged, stop streaming after <select id='unplug'></select></label><i class='tip' data-tip='A countdown shows on the phone when it goes on battery while streaming; when it ends the stream stops. It starts again when the power returns.'>i</i></div>

<h2>Network</h2>
<div class='row'><label><input type='checkbox' id='wifivideo'> Video over Wi-Fi</label><span class='st' id='st-wifivideo'></span><i class='tip' data-tip='The video for viewers that are not on the USB cable. The stream has no login (OBS and VLC cannot send one), so while it is on anyone on your network can watch. Off: only the PC on the USB cable gets video.'>i</i><span class='hint' id='h-wifivideo'></span>
<div class='links' id='wifivideolinks'></div><div id='wifivideowarn'></div></div>
<div class='row'><label><input type='checkbox' id='webpage' checked disabled> Web page (settings and status)</label><span class='st' id='st-webpage'></span><i class='tip' data-tip='This page and /status. It can only be switched off on the phone (Network), since this page would cut itself off. The video is never affected.'>i</i><span class='hint'>switch off on the phone only</span>
<div class='links' id='weblinks'></div></div>
<div class='row'><label><input type='checkbox' id='logpage'> Log page</label><span class='st' id='st-logpage'></span><span class='hint' id='h-logpage'></span><i class='tip' data-tip='The app log at /log, also used by tools\Get-Phone-Log.cmd. Off by default.'>i</i>
<div class='links' id='loglinks'></div></div>
<div class='row'><label><input type='checkbox' id='statspage'> Stats page</label><span class='st' id='st-statspage'></span><span class='hint' id='h-statspage'></span><i class='tip' data-tip='Graphs of the last 12 hours (frame rate, bitrate, latency, battery, power, CPU, memory, heat). Samples every 30 s only while this is on. Off by default.'>i</i>
<div class='links' id='statslinks'></div></div>
<div class='row'><div class='head'><span>Login for changes<i class='tip' data-tip='Changes need this login, over the USB cable and over Wi-Fi: the browser asks for it before the first change. An empty password removes the login.'>i</i></span></div>
<div class='sub' id='loginstate'></div>
<div id='loginform'><input class='tx' id='lu' placeholder='User name' autocomplete='username'> <input class='tx' id='lp' type='password' placeholder='New password' autocomplete='new-password'> <input class='tx' id='lp2' type='password' placeholder='Repeat password' autocomplete='new-password'>
<div class='btnrow'><button id='lb'>Save login</button><button id='lrm'>Reset to admin / admin</button></div></div></div>

<h2 id='viewer'>Viewer setup</h2>
<div class='row'><div class='sub'>Which address: <b>USB</b> addresses (127.0.0.1) work only on the PC the phone is plugged into; <b>Wi-Fi</b> addresses work from any device on the same network (see Connect above). 4K at Auto bitrate needs about 37 Mbps: use the USB cable, or 1080p / a lower bitrate over Wi-Fi.
The video is raw H.264 with no container, so players must be told the format, as below.</div></div>
<div class='row'><div class='head'><span>OBS Studio (as a webcam)</span></div>
<div class='sub'>Add a Media Source, untick Local File, then:</div>
<div class='addr'><b>Input</b><code>tcp://127.0.0.1:8020</code></div>
<div class='sub'>(over Wi-Fi: the Wi-Fi video address from Connect)</div>
<div class='addr'><b>Input format</b><code>h264</code></div>
<div class='addr'><b>FFmpeg options (lowest delay)</b><code>fflags=nobuffer flags=low_delay analyzeduration=0 probesize=500000</code></div>
<div class='sub'>Tick Restart playback when source becomes active. Then Start Virtual Camera and choose OBS Virtual Camera in your video app.</div></div>
<div class='row'><div class='head'><span>VLC</span></div>
<div class='sub'>Media &gt; Open Network Stream, enter the video address, tick Show more options and add this to Edit Options (without it VLC connects but never plays):</div>
<div class='addr'><b>Edit Options</b><code>:demux=h264</code></div>
<div class='sub'>Or from a command prompt:</div>
<div class='addr'><b>Command</b><code>vlc --demux=h264 tcp://127.0.0.1:8020</code></div></div>
<div class='row'><div class='head'><span>ffplay (quick test)</span></div>
<div class='addr'><b>Command</b><code>ffplay -fflags nobuffer -flags low_delay -f h264 tcp://127.0.0.1:8020</code></div></div>
<div id='msg'></div><div id='tipbox'></div>
<script>
var st=null,busyUntil=0,timer=null;
function $(i){return document.getElementById(i)}
function esc(t){return String(t==null?'':t).replace(/[&<>'`]/g,function(c){return '&#'+c.charCodeAt(0)+';'})}
function fmt(n,d){return n==null?'n/a':Number(n).toFixed(d)}
function e2s(ms){var a=st.expMin,b=st.expMax;return b>a?100*Math.log(ms/a)/Math.log(b/a):0}
function s2e(v){var a=st.expMin,b=st.expMax;return a*Math.pow(b/a,v/100)}
function fill(sel,items,idx){
 var key=items.join('|');
 if(sel.dataset.k!==key){sel.dataset.k=key;sel.innerHTML='';items.forEach(function(t,i){var o=document.createElement('option');o.value=i;o.textContent=t;sel.appendChild(o)})}
 sel.value=idx;
}
function meter(p,cls){p=Math.max(0,Math.min(100,p||0));return '<div class=\'meter '+(cls||'')+'\'><div style=\'width:'+p+'%\'></div></div>'}
function tile(l,v,w,g,tip){return '<div class=\'tile'+(w?' warn':'')+'\''+(tip?' data-tip=\''+esc(tip)+'\'':'')+'><b>'+esc(l)+'</b>'+(v===''?'':'<span>'+esc(v)+'</span>')+(g||'')+'</div>'}
function badge(t,c){return '<div class=\'badge '+(c||'')+'\'>'+esc(t)+'</div>'}
function notice(t,bad,btn){return '<div class=\'notice'+(bad?' bad':'')+'\'><span class=\'ni\'>!</span><span>'+esc(t)+'</span>'+(btn||'')+'</div>'}
function render(){
 if(!st)return;
 var w=st.canWrite||st.loginSet; // with a login set, a change makes the browser ask for it (over USB too)
 $('ro').style.display=st.canWrite?'none':'flex';
 $('rotext').innerHTML=st.loginSet?'Changes over Wi-Fi need the login (the browser asks for it'+(st.defaultLogin?'; default admin / admin':'')+').':'View only over Wi-Fi. To change settings from here, set a login on the phone (Network &gt; Login), or use <code>http://127.0.0.1:8020/settings</code> on the PC the phone is plugged into.';
 $('loginb').style.display=!st.canWrite&&st.loginSet?'inline-block':'none';
 if(st.version)$('ver').textContent='v'+st.version;
 var fm=/^(\d+) x (\d+) @ (\d+)/.exec(st.format||'');
 var res=fm?(fm[2]>=2160?'4K':fm[2]+'p')+fm[3]+(st.safeMode?' SAFE':''):'';
 var state=st.protect&&st.streaming?'Heat protection':st.streaming?'Streaming':'Stopped',dc=st.protect?'dot hot':st.streaming?'dot on':'dot';
 var up=Math.round(st.uptime/60),upt=up>=120?Math.round(up/6)/10+' h':up+' min';
 var bp=st.battery==null?0:st.battery,bcls=st.batteryFalling||bp<20?'low':bp<40?'mid':'';
 var batt='<div class=\'batt\'>'+meter(bp,bcls)+'<span class=\'cap\'></span></div>';
 var wb=st.wifiBars==null?0:st.wifiBars,bars='<div class=\'bars'+(wb<2?' weak':'')+'\'>';
 for(var k=1;k<=4;k++)bars+='<i class=\''+(k<=wb?'on':'')+'\' style=\'height:'+(2+k*3)+'px\'></i>';bars+='</div>';
 var cp=st.cpuPhone==null?null:Math.round(st.cpuPhone),ca=st.cpuApp==null?null:Math.round(st.cpuApp);
 var ml=st.memoryLimitMB||1536,mp=100*(st.memoryMB||0)/ml;
 var lat=st.streaming&&st.latencyMs!=null&&st.latencyMs>=0?st.latencyMs:null;
 var charging=st.batteryStatus=='Charging',pw=null,pl=charging?'Into battery':'Battery draw';
 if(!charging&&st.drawWatts!=null)pw=Math.abs(st.drawWatts);else if(st.netWatts!=null)pw=charging?st.netWatts:Math.abs(st.netWatts);
 var pv=pw==null?'measuring':(charging&&pw>=0?'+':'')+pw.toFixed(1)+' W';
 var pbar=pw==null?meter(0):meter(100*Math.abs(pw)/15,charging?(pw<0?'low':pw<1?'mid':''):(pw>8?'low':'mid'));
 var eng=st.safeMode?'Safe mode':st.streaming?(st.engine=='low-latency'?'Fast':st.engine):'Ready';
 var hl=st.heatLevel||0;
 $('stat').innerHTML='<div class=\'card\'><div class=\'top\'><span class=\''+dc+'\'></span><span class=\'state\'>'+esc(state)+'</span>'
  +(res&&st.streaming?'<span class=\'pill\'>'+esc(res)+'</span>':'')
  +'<span class=\'pill\'>up '+esc(upt)+'</span>'
  +'<span class=\'pill r\'>'+esc(st.viewers+' viewer'+(st.viewers==1?'':'s'))+'</span></div>'
  +(st.streaming?'<div class=\'big\'>'+esc(Math.round(st.fps))+'<small>fps</small>'+esc(fmt(st.mbps,1))+'<small>Mbps</small></div>':'')
  +'<div class=\'grid\'>'
  +tile('Battery',st.battery==null?'n/a':st.battery+'% '+(charging?'chg':String(st.batteryStatus).toLowerCase()),st.batteryFalling,batt)
  +tile('Wi-Fi',st.wifiBars==null?'off':wb+' of 4',st.wifiBars!=null&&wb<2,bars,'The phone\'s Wi-Fi signal. Over USB the video does not use Wi-Fi.')
  +tile('Temp','',false,badge(st.heatBadge||'n/a',hl==2?'bad':hl==1?'mid':''),'Windows gives apps no temperature. Hot means charging has paused on a good charger for 20 s, which Windows does when the battery is hot.')
  +tile('CPU phone',cp==null?'n/a':cp+'%',false,meter(cp,cp>70?'mid':''),'Whole phone. The camera and encoder do most of their work outside the CPU.')
  +tile('CPU app',ca==null?'n/a':ca+'%',false,meter(ca,ca>50?'mid':''))
  +tile('Memory',Math.round(st.memoryMB||0)+' MB',mp>80,meter(mp,mp>80?'low':mp>60?'mid':''),'App memory, of '+Math.round(ml)+' MB allowed.')
  +tile('Engine','',false,badge(eng,eng=='Fast'||eng=='Ready'?'':'mid'),st.safeMode?'Safe mode: the standard MP4 engine, used after a failed start. Reopen the app or use the button to return.':'Fast: the low-latency engine hands each encoded frame straight to the stream.')
  +tile('Latency',lat==null?'n/a':lat+' ms',lat!=null&&lat>250,meter(lat==null?0:lat/3,lat>250?'low':lat>150?'mid':''),'Camera sensor to encoded frame, measured inside the phone (10 s average). USB and OBS add their own delay.')
  +tile(pl,pv,st.batteryFalling,pbar,(st.powerText||'')+' Bar scale: 0-15 W.')
  +'</div></div>';
 var nh='';
 if(st.alert)nh+=notice(st.alert,true,st.safeMode&&w?'<button id=\'engine\'>Return to normal</button>':'');
 if(st.defaultLogin)nh+=notice('Default login admin / admin in use for changes over Wi-Fi. Change it under Network > Login.',false);
 if(st.chargerNotice)nh+=notice(st.chargerNotice,false);
 if(st.unplugLeftSec!=null)nh+=notice('The phone is on battery: streaming stops in '+Math.round(st.unplugLeftSec)+' s.',false,w?'<button id=\'keep\'>Keep streaming</button>':'');
 $('warn').innerHTML=nh;
 if($('engine'))$('engine').onclick=function(){send('engine=normal','Switching engine...')};
 if($('keep'))$('keep').onclick=function(){send('keepstreaming=1')};
 if(Date.now()<busyUntil)return; // don't overwrite what the person is changing
 $('stream').textContent=st.streaming?'Stop stream':'Start stream';$('stream').disabled=!w;
 fill($('format'),st.formats,st.formatIndex);fill($('bitrate'),st.bitrates.map(function(b){return b=='Auto'?'Bitrate: auto':b}),st.bitrateIndex);
 fill($('keyframe'),st.keyframes,st.keyframeIndex);fill($('unplug'),st.unplugChoices,st.unplugIndex);
 ['format','bitrate','keyframe','unplug'].forEach(function(i){$(i).disabled=!w});
 var wifi=st.wifi||'',usb='http://127.0.0.1:'+st.usbPort,wf=wifi?'http://'+esc(wifi)+':'+st.logPort:'';
 var ad=function(l,v){return '<div class=\'addr\'><b>'+l+'</b><code>'+v+'</code></div>'};
 $('connect').innerHTML='<div class=\'grp\'><b>USB cable</b>'+ad('Video','tcp://127.0.0.1:'+st.usbPort)+ad('Settings (can change settings)',usb+'/settings')+'</div>'
  +'<div class=\'grp\'><b>Wi-Fi</b>'+(wifi?ad('Video'+(st.wifiVideoOn?' (no login: anyone on the network can watch)':''),st.wifiVideoOn?'tcp://'+esc(wifi)+':'+st.wifiPort:'off (Network &gt; Video over Wi-Fi)')+ad('Settings (changes after logging in)',wf+'/settings'):'<div class=\'sub\'>The phone is not on Wi-Fi.</div>')+'</div>';
 // grouped: the USB addresses together, then the Wi-Fi ones
 var lk=function(paths){var a=function(b,p){return '<a href=\''+b+p+'\' target=\'_blank\'>'+b+p+'</a>'},sp='&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;';
  var h='USB&nbsp;&nbsp;&nbsp;'+paths.map(function(p){return a(usb,p)}).join('<br>'+sp);
  if(wf)h+='<br>Wi-Fi&nbsp;'+paths.map(function(p){return a(wf,p)}).join('<br>'+sp);return h};
 $('weblinks').innerHTML=lk(['/settings','/status']);
 $('loglinks').innerHTML=lk(['/log']);$('loglinks').className='links'+(st.logOn?'':' off');
 $('statslinks').innerHTML=lk(['/stats']);$('statslinks').className='links'+(st.statsOn?'':' off');
 $('loginstate').textContent=st.defaultLogin?'Default login admin / admin: change it, or anyone on your network can change settings.':'Over Wi-Fi the browser asks for this login before changes'+(st.user?' (user '+st.user+')':'')+'.';
 $('loginform').style.display=w?'block':'none';$('lrm').style.display=st.defaultLogin?'none':'inline-block';
 if(w&&st.user&&!$('lu').value&&document.activeElement!==$('lu'))$('lu').value=st.user;
 $('camnow').textContent='Now: '+(st.camera||'n/a');
 $('camauto').checked=st.focusAuto&&st.exposureAuto&&st.wbAuto;
 $('fa').checked=st.focusAuto;$('ea').checked=st.exposureAuto;$('wa').checked=st.wbAuto;
 $('fs').value=st.focus;$('es').value=Math.max(0,Math.min(100,e2s(st.exposureMs)));
 $('ws').min=st.wbMin;$('ws').max=st.wbMax;$('ws').value=st.wb;
 $('camauto').disabled=!w;
 $('fa').disabled=!w||!st.focusSupported;$('fs').disabled=!w||!st.focusSupported||st.focusAuto;$('fonce').disabled=!w||!st.focusSupported;
 $('ea').disabled=!w||!st.exposureSupported;$('es').disabled=!w||!st.exposureSupported||st.exposureAuto;
 $('wa').disabled=!w||!st.wbSupported;$('ws').disabled=!w||!st.wbSupported||st.wbAuto;
 $('sf').checked=st.saveFocus;$('sf').disabled=!w;
 $('torch').checked=st.torchOn;$('torch').disabled=!w||!st.torchSupported||(!st.streaming&&!st.torchOn);
 $('torchnote').textContent=!st.torchSupported?'This camera has no flashlight control.':st.torchOn?'On - switches off in '+st.torchLeftSec+' s.':!st.streaming?'Start the stream to use the flashlight.':'';
 $('ois').checked=st.saveOis;$('ois').disabled=!w||!st.oisSupported;
 $('powersave').checked=st.powerSave;$('autostart').checked=st.autoStart;$('preview').checked=st.preview;$('heatauto').checked=st.heatAuto;
 ['powersave','autostart','preview','heatauto'].forEach(function(i){$(i).disabled=!w});
 $('lens').value=st.front?'front':'rear';$('lens').disabled=!w;
 // each switch shows its real state on the phone as an ON / OFF pill, also when this page can't change it
 var pill=function(id,on){var e=$('st-'+id);e.textContent=on?'ON':'OFF';e.className='st'+(on?'':' off')};
 pill('wifivideo',st.wifiVideoOn);pill('webpage',st.webOn!==false);pill('logpage',st.logOn);pill('statspage',st.statsOn);
 ['wifivideo','logpage','statspage'].forEach(function(i){$('h-'+i).textContent=w?'':'log in to change'});
 $('wifivideo').checked=st.wifiVideoOn;$('logpage').checked=st.logOn;$('statspage').checked=st.statsOn;
 $('wifivideo').disabled=$('logpage').disabled=$('statspage').disabled=!w;
 $('wifivideolinks').innerHTML=wifi?'Wi-Fi&nbsp;tcp://'+esc(wifi)+':'+st.wifiPort+'&nbsp;&nbsp;(USB video is always on)':'(the phone is not on Wi-Fi)';$('wifivideolinks').className='links'+(st.wifiVideoOn?'':' off');
 $('wifivideowarn').innerHTML=st.wifiVideoOn?notice('Unauthenticated network video: anyone on this Wi-Fi can watch the camera. Switch off if you only use the USB cable.',false):'';
 labels();
}
function labels(){
 if(!st)return;
 $('fl').textContent='Focus: '+($('fa').checked?'automatic'+(st.focusHeld?' (held)':''):Math.round($('fs').value)+' (0 near - 100 far)');
 var ms=s2e($('es').value);
 $('el').textContent='Exposure: '+($('ea').checked?'automatic':ms.toFixed(ms<10?2:1)+' ms (1/'+Math.round(1000/ms)+' s)');
 $('wl').textContent='White balance: '+($('wa').checked?'automatic':$('ws').value+' K');
}
function send(body,note){
 busyUntil=Date.now()+4000;if(note)$('msg').textContent=note;
 fetch('/settings/set',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded','X-Requested-With':'W10MWebCam'},body:body})
 .then(function(r){if(r.status==401){location.href='/login';throw new Error('Login needed')}return r.ok?r.json():r.text().then(function(x){throw new Error(x)})})
 .then(function(j){st=j;busyUntil=0;render();if(j.result)$('msg').textContent=j.result})
 .catch(function(e){busyUntil=0;$('msg').textContent=e.message});
}
function load(){
 fetch('/settings/state',{cache:'no-store'}).then(function(r){return r.json()}).then(function(j){st=j;render()})
 .catch(function(){$('stat').innerHTML=notice('The phone is not answering. Is the app open and the cable plugged in?',true)});
}
function later(body){busyUntil=Date.now()+4000;clearTimeout(timer);timer=setTimeout(function(){send(body)},400)}
function onoff(b){return b?'on':'off'}
$('stream').onclick=function(){send('stream='+(st&&st.streaming?'stop':'start'),st&&st.streaming?'Stopping...':'Starting...')};
$('loginb').onclick=function(){location.href='/login'};
$('toviewer').onclick=function(e){e.preventDefault();var h=$('viewer');if(h.nextSibling&&h.nextSibling.style&&h.nextSibling.style.display==='none')h.click();h.scrollIntoView({behavior:'smooth'})};
$('lb').onclick=function(){var u=$('lu').value.trim(),p=$('lp').value;
 if(p!==$('lp2').value){$('msg').textContent='The two passwords differ';return}
 if(p.length<4){$('msg').textContent='The password needs at least 4 characters';return}
 $('lp').value=$('lp2').value='';send('user='+encodeURIComponent(u||'admin')+'&password='+encodeURIComponent(p))};
$('lrm').onclick=function(){$('lp').value=$('lp2').value='';send('password=','Resetting the login...')};
$('wifivideo').onchange=function(){send('wifivideo='+onoff(this.checked),this.checked?'':'Switching off video over Wi-Fi...')};
['format','bitrate','keyframe'].forEach(function(i){$(i).onchange=function(){send(i+'='+this.value,'Applying - the stream restarts for a moment...')}});
$('unplug').onchange=function(){send('unplug='+this.value)};
$('camauto').onchange=function(){send('camera='+(this.checked?'auto':'manual'))};
$('fa').onchange=function(){send('focus='+(this.checked?'auto':'manual'))};
$('ea').onchange=function(){send('exposure='+(this.checked?'auto':'manual'))};
$('wa').onchange=function(){send('wb='+(this.checked?'auto':'manual'))};
$('fs').oninput=function(){labels();later('focus='+this.value)};
$('es').oninput=function(){labels();later('exposure='+s2e(this.value).toFixed(3))};
$('ws').oninput=function(){labels();later('wb='+this.value)};
$('fonce').onclick=function(){send('focus=once','Focusing...')};
$('sf').onchange=function(){send('savefocus='+onoff(this.checked))};
$('torch').onchange=function(){send('torch='+onoff(this.checked))};
$('ois').onchange=function(){send('saveois='+onoff(this.checked),'Applying - the stream restarts for a moment...')};
$('powersave').onchange=function(){send('powersave='+onoff(this.checked))};
$('autostart').onchange=function(){send('autostart='+onoff(this.checked))};
$('preview').onchange=function(){send('preview='+onoff(this.checked))};
$('heatauto').onchange=function(){send('heatauto='+onoff(this.checked))};
$('lens').onchange=function(){send('lens='+this.value,'Switching camera - the stream restarts...')};
$('logpage').onchange=function(){send('logpage='+onoff(this.checked))};
$('statspage').onchange=function(){send('statspage='+onoff(this.checked))};
// Every section header folds its section away (arrow at the right); the choice is remembered in this browser.
(function(){
 var saved={};try{saved=JSON.parse(localStorage.getItem('w10m-fold')||'{}')}catch(e){}
 var hs=Array.prototype.slice.call(document.getElementsByTagName('h2'));
 hs.forEach(function(h){
  var name=h.textContent,body=document.createElement('div'),n=h.nextSibling;
  while(n&&!(n.nodeType===1&&(n.tagName==='H2'||n.id==='msg'||n.tagName==='SCRIPT'))){var nx=n.nextSibling;body.appendChild(n);n=nx}
  h.parentNode.insertBefore(body,n);
  var ar=document.createElement('span');ar.className='ar';h.appendChild(ar);
  h.setAttribute('role','button');
  function show(open){body.style.display=open?'':'none';ar.textContent=open?'▾':'▴';h.setAttribute('aria-expanded',open?'true':'false')}
  show(saved[name]!==false);
  h.onclick=function(){var open=body.style.display==='none';show(open);saved[name]=open;try{localStorage.setItem('w10m-fold',JSON.stringify(saved))}catch(e){}}
 })
})();
// Hover (or tap) anything with data-tip to see its note; the box stays inside the window.
(function(){
 var box=$('tipbox'),cur=null;
 function find(e){var n=e.target;while(n&&n!==document){if(n.getAttribute&&n.getAttribute('data-tip'))return n;n=n.parentNode}return null}
 function show(n){var t=n.getAttribute('data-tip');if(!t){hide();return}cur=n;box.textContent=t;box.style.display='block';
  var r=n.getBoundingClientRect(),w=box.offsetWidth,h=box.offsetHeight;
  var x=Math.max(8,Math.min(window.innerWidth-w-8,r.left+r.width/2-w/2)),y=r.bottom+6;
  if(y+h>window.innerHeight-8)y=Math.max(8,r.top-h-6);box.style.left=x+'px';box.style.top=y+'px'}
 function hide(){cur=null;box.style.display='none'}
 document.addEventListener('mouseover',function(e){var n=find(e);if(n)show(n);else if(cur)hide()});
 document.addEventListener('click',function(e){var n=find(e);if(n&&(' '+n.className+' ').indexOf(' tip ')>=0){show(n);e.preventDefault();e.stopPropagation()}else if(!n)hide()},true);
 window.addEventListener('scroll',hide,true);
})();
load();setInterval(load,2000);
</script></body></html>
";
    }
}
