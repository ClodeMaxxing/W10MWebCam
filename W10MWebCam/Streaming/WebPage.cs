namespace W10MWebCam.Streaming
{
    /// <summary>
    /// The settings web page (http://127.0.0.1:8020/settings over USB, http://PHONE-IP:5001/settings over Wi-Fi): the phone
    /// screen, mirrored. The same status card with the stream row as the button, the same service groups, the Preview /
    /// Screensaver / Flashlight buttons and the same drawers. The two differences: a Log in button (the page is view only
    /// without the login) and the web page switch, which only the phone can turn off. The state comes from /settings/state.
    /// </summary>
    internal static class WebPage
    {
        public const string Html = @"<!doctype html>
<html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>W10MWebCam</title>
<style>
body{font-family:'Segoe UI',system-ui,sans-serif;background:#050805;color:#d6e8d9;margin:0;padding:12px;max-width:420px}
a{color:#9cf0b0}
.card{background:#071109;border:1px solid #1f3a26;border-radius:8px;margin:0 0 6px;overflow:hidden}
.ttl{display:flex;justify-content:space-between;align-items:baseline;padding:8px 10px 6px;background:#050a06;border-bottom:1px solid #1f3a26}
.ttl b{font-size:20px;font-weight:600;color:#e4f5e7}.ttl i{font-style:normal;color:#39ff14;font-size:11px;font-weight:bold}
.srow{position:relative;display:flex;gap:9px;align-items:center;height:46px;padding:0 10px 0 16px;background:#0a160d;border-bottom:1px solid #1f3a26;cursor:pointer;user-select:none}
.srow:before{content:'';position:absolute;left:0;top:9px;bottom:9px;width:4px;border-radius:0 2px 2px 0;background:#16301e}
.srow.on:before{background:#39ff14}
.srow b{font-weight:600;letter-spacing:1px;font-size:14px;color:#8fb596}.srow.on b{color:#9cf0b0}.srow.hot b{color:#ff6b6b}
.srow .fmt{margin-left:auto;border:1px solid #2a4a32;border-radius:9px;padding:1px 7px;font-size:11px;color:#8fb596;font-family:Consolas,'Cascadia Mono',monospace;background:#0b1a10}
.srow.on .fmt{border-color:#39ff14;color:#cfffd2}
.srow.busy{opacity:.5;pointer-events:none}
.rl{width:12px;height:12px;border-radius:50%;background:#3a1414;border:1px solid #5a2020;flex:none}
.on .rl{background:#ff3030;border-color:#ff8080;animation:pulse 2.4s ease-in-out infinite}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.25}}
.body{padding:6px 8px 8px}
.g3{display:grid;grid-template-columns:repeat(3,1fr);gap:4px}.g3+.g3{margin-top:4px}
.tile{background:#0b1a10;border:1px solid #16301e;border-radius:6px;padding:5px 7px;min-width:0;overflow:hidden}
.tile.k{border-color:#1f3a26}.tile.k span{font-size:14px;color:#fff}
.tile b{display:block;font-size:9px;letter-spacing:1px;text-transform:uppercase;color:#6f8f77;font-weight:600}
.tile span{font-family:Consolas,'Cascadia Mono',monospace;font-size:12px;color:#d6e8d9;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;display:block}
.tile.dim span{color:#7fae86}.tile.warn span{color:#ff8a8a}
.meter{height:6px;background:#06100a;border:1px solid #1f3a26;border-radius:3px;overflow:hidden;margin-top:4px}
.meter>div{height:100%;background:#39ff14;border-radius:2px}
.meter.mid>div{background:#e8d040}.meter.low>div{background:#ff5555}
.bars{display:flex;align-items:flex-end;gap:3px;height:14px;margin-top:4px}.bars i{display:block;width:6px;background:#16301e;border-radius:1px}
.bars i.on{background:#39ff14}.bars.weak i.on{background:#e8d040}
.badge{display:inline-flex;align-items:center;gap:5px;margin-top:4px;padding:1px 8px 1px 6px;border-radius:9px;font-size:11px;font-weight:600;border:1px solid #39ff14;color:#cfffd2;background:#0b1a10}
.badge::before{content:'';width:6px;height:6px;border-radius:50%;background:#39ff14}
.badge.mid{color:#e8d040;border-color:#7d6e2e}.badge.mid::before{background:#e8d040}
.badge.bad{color:#ff6b6b;border-color:#7d2e2e}.badge.bad::before{background:#ff5555;animation:pulse 1s ease-in-out infinite}
.notice{display:flex;align-items:center;gap:8px;margin:6px 0 0;padding:6px 9px;border:1px solid #7d6e2e;border-left:3px solid #e8d040;border-radius:6px;background:#1a1808;color:#e8d8a0;font-size:12px}
.notice .ni{flex:none;width:16px;height:16px;line-height:16px;text-align:center;border-radius:50%;background:#e8d040;color:#1a1808;font-weight:bold;font-size:11px}
.notice.bad{border-color:#7d2e2e;border-left-color:#ff5555;background:#1a0808;color:#ffb0b0}.notice.bad .ni{background:#ff5555;color:#1a0808}
.notice code{font-family:Consolas,'Cascadia Mono',monospace;color:#fff3b0}
.notice button{margin:0 0 0 auto;padding:4px 10px;font-size:12px;width:auto}
.grp{background:#071109;border:1px solid #1f3a26;border-radius:8px;padding:6px 8px 3px;margin:0 0 6px}
.grp h4{margin:0 0 4px 2px;font-size:10px;font-weight:600;letter-spacing:1px;color:#7fae86}
.svc{display:flex;align-items:center;gap:7px;border:1px solid #2a4a32;border-radius:12px;padding:3px 9px;margin:0 0 4px;font-family:Consolas,'Cascadia Mono',monospace;font-size:11px;color:#6e9a76;background:#0b1a10}
.svc:before{content:'';width:7px;height:7px;border-radius:50%;background:#555;flex:none}
.svc b{font-weight:400;color:#8fb596;min-width:34px}.svc code{font-family:inherit;color:inherit;user-select:all;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.svc span{margin-left:auto;color:#7f9a86;font-family:'Segoe UI',sans-serif;flex:none}
.svc.on{border-color:#39ff14;color:#cfffd2}.svc.on:before{background:#39ff14}
.svc.q{border-color:#7d6e2e;color:#e8d8a0}.svc.q:before{background:#e0b030}
.bar{position:relative;display:flex;justify-content:space-between;align-items:center;height:46px;padding:0 10px 0 16px;background:#071109;border:1px solid #1f3a26;border-radius:8px;margin:0 0 6px;cursor:pointer;user-select:none;overflow:hidden}
.bar:before{content:'';position:absolute;left:0;top:9px;bottom:9px;width:4px;border-radius:0 2px 2px 0;background:#16301e}
.bar .n{font-size:14px;font-weight:500;color:#d8f5dc}.bar .s{font-size:11px;letter-spacing:.5px;color:#7fae86;font-family:Consolas,'Cascadia Mono',monospace}
.bar.on:before{background:#39ff14}.bar.on .s{color:#39ff14}
.bar.dis,.sw.dis{opacity:.45}.pk.dis{opacity:.5}
.dr{background:#071109;border:1px solid #1f3a26;border-radius:8px;margin:0 0 6px}
.dh{display:flex;justify-content:space-between;align-items:center;padding:10px;background:#0a160d;color:#a8f0b0;font-weight:600;letter-spacing:1px;font-size:13px;cursor:pointer;user-select:none;border-radius:7px 7px 0 0}
.dh.c{border-radius:7px}
.dh i{font-style:normal;color:#a8f0b0}
.db{padding:6px 8px 4px;border-top:1px solid #1f3a26}
.sw{position:relative;display:flex;justify-content:space-between;align-items:center;gap:8px;min-height:40px;border:1px solid #142a1a;border-radius:6px;background:#0a160d;padding:7px 9px 7px 15px;margin:0 0 4px;cursor:pointer;user-select:none;overflow:hidden}
.sw:before{content:'';position:absolute;left:0;top:7px;bottom:7px;width:4px;border-radius:0 2px 2px 0;background:#16301e}
.sw .n{font-size:12px;color:#d8f5dc;line-height:1.3}.sw .s{font-size:11px;color:#7fae86;font-family:Consolas,'Cascadia Mono',monospace;flex:none}
.sw.on:before{background:#39ff14}.sw.on .s{color:#39ff14}
.fld{border:1px solid #142a1a;border-radius:6px;background:#0a160d;padding:6px 9px 7px;margin:0 0 4px}
.fld b{display:block;font-size:9px;letter-spacing:1px;text-transform:uppercase;color:#6f8f77;font-weight:600}
.fld code{display:block;font-family:Consolas,'Cascadia Mono',monospace;font-size:12px;color:#cfffd2;margin-top:3px;word-break:break-all;user-select:all;white-space:pre-wrap}
.pk{position:relative;cursor:pointer;user-select:none}.pk.open{border-color:#39ff14;z-index:30}
.pk .pv{display:flex;justify-content:space-between;align-items:center;font-family:Consolas,'Cascadia Mono',monospace;font-size:14px;color:#cfffd2;margin-top:3px}
.pk .pv i{font-style:normal;color:#3fbf5f}
.pk .list{display:none;position:absolute;left:-1px;right:-1px;top:100%;margin-top:3px;background:#071109;border:1px solid #39ff14;border-radius:6px;z-index:20;max-height:320px;overflow-y:auto;box-shadow:0 8px 20px rgba(0,0,0,.7)}
.pk.open .list{display:block}
.pk .list div{position:relative;padding:9px 12px 9px 16px;font-family:Consolas,'Cascadia Mono',monospace;font-size:13px;color:#9cc3a2;border-bottom:1px solid #0e2014}
.pk .list div:last-child{border-bottom:0}.pk .list div:hover{background:#0e2014;color:#cfffd2}
.pk .list div.a{background:#163a1e;color:#cfffd2}.pk .list div.a:before{content:'';position:absolute;left:0;top:9px;bottom:9px;width:3px;background:#39ff14;border-radius:0 2px 2px 0}
.sl .t{display:flex;justify-content:space-between;align-items:center}
.sl .val{display:flex;align-items:baseline;gap:6px;margin-top:3px}
.sl .val em{font-style:normal;font-family:Consolas,'Cascadia Mono',monospace;font-size:14px;color:#fff}
.sl .val small{font-size:10px;color:#7fae86;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.seg{display:inline-flex;border:1px solid #1f3a26;border-radius:10px;overflow:hidden;font-size:10px;cursor:pointer}
.seg span{padding:2px 9px;color:#7fae86}.seg span.a{background:#1f3a26;color:#cfffd2}
input[type=range]{width:100%;accent-color:#39ff14;margin:8px 0 0}input[type=range]:disabled{opacity:.35}
.sl.off .val em{color:#7fae86}
input[type=text],input[type=password],input:not([type]){background:#06100a;color:#d6e8d9;border:1px solid #1f3a26;border-radius:6px;padding:7px 9px;font-size:13px;width:100%;box-sizing:border-box;margin:4px 0 0}
button{background:#0e2a14;color:#e6ffe8;border:1px solid #2e7d46;border-radius:6px;padding:8px 12px;font-size:13px;cursor:pointer;width:100%;margin:4px 0 0}
button:hover:not(:disabled){background:#18452b}button:disabled{opacity:.35}
.tip{display:inline-block;width:14px;height:14px;line-height:13px;text-align:center;border-radius:50%;border:1px solid #3fbf5f;color:#3fbf5f;font-size:9px;font-style:normal;font-weight:bold;cursor:help;margin-left:6px;vertical-align:middle;flex:none}
#tipbox{position:fixed;display:none;max-width:260px;background:#0b1a10;border:1px solid #2e7d46;color:#d6e8d9;padding:8px 10px;border-radius:6px;font-size:12px;line-height:1.45;z-index:50;box-shadow:0 6px 16px rgba(0,0,0,.7);pointer-events:none}
.bad{color:#ff6b6b;font-size:12px;font-weight:bold;margin:2px 0 4px}
#msg{color:#e9a;font-size:12px;min-height:16px;margin:4px 2px}
.lg{font-family:Consolas,'Cascadia Mono',monospace;font-size:11px;color:#b9cfbd;white-space:pre-wrap;word-break:break-word;max-height:300px;overflow:auto}
</style></head><body>

<div class='card'>
 <div class='ttl'><b>W10MWebCam</b><i id='ver'></i></div>
 <div class='srow' id='srow'><span class='rl'></span><b id='state'>START STREAM</b><span class='fmt' id='fmt'>-</span></div>
 <div class='body'>
  <div class='g3'><div class='tile k dim'><b>FPS</b><span id='tfps'>-</span></div><div class='tile k dim'><b>MBPS</b><span id='tmbps'>-</span></div><div class='tile k dim'><b>VIEWERS</b><span id='tview'>-</span></div></div>
  <div class='g3' id='tiles'></div>
  <div id='warn'></div>
 </div>
</div>
<div class='notice' id='ro' style='display:none'><span class='ni'>i</span><span id='rotext'>View only.</span><button id='loginb'>Log in</button></div>

<div class='grp'><h4>H.264 VIDEO</h4>
 <div class='svc' id='rw'><b>Wi-Fi</b><code id='rwa'></code><span id='rwn'></span></div>
 <div class='svc' id='ru'><b>USB</b><code id='rua'></code><span id='run'></span></div>
</div>
<div class='grp'><h4>WEB UI</h4>
 <div class='svc' id='ww'><b>Wi-Fi</b><code id='wwa'></code><span id='wwn'></span></div>
 <div class='svc' id='wu'><b>USB</b><code id='wua'></code><span id='wun'></span></div>
</div>

<div class='bar' id='bpv'><span class='n'>Preview</span><span class='s' id='bpvs'>OFF</span></div>
<div class='bar' id='bss'><span class='n'>Screensaver</span><span class='s' id='bsss'>ON</span></div>
<div class='bar' id='btt'><span class='n'>Flashlight</span><span class='s' id='btts'>OFF</span></div>

<div class='dr'><div class='dh' data-f='video'>VIDEO<i>&#x25B8;</i></div><div class='db' id='video'>
 <div class='bad' id='ta3' style='display:none'>Heat protection: 1080p / 6 Mbps</div>
 <div class='fld pk' id='lsel' data-k='lens'><b>Camera<i class='tip' data-tip='Rear: the main camera, up to 4K30. Front: the selfie camera (1080p at most on the Lumia 950). Switching restarts the camera and the stream.'>i</i></b><div class='pv'><span>-</span><i>&#x25BE;</i></div><div class='list'></div></div>
 <div class='fld pk' id='fsel' data-k='format'><b>Resolution</b><div class='pv'><span>-</span><i>&#x25BE;</i></div><div class='list'></div></div>
 <div class='fld pk' id='bsel' data-k='bitrate'><b>Bitrate</b><div class='pv'><span>-</span><i>&#x25BE;</i></div><div class='list'></div></div>
 <div class='fld pk' id='ksel' data-k='keyframe'><b>Keyframe every<i class='tip' data-tip='A full picture this often. Shorter: OBS joins and recovers faster, at slightly more data. It does not change the delay.'>i</i></b><div class='pv'><span>-</span><i>&#x25BE;</i></div><div class='list'></div></div>
 <div class='sw' data-k='autostart' id='ast'><span class='n'>Start streaming when the app opens</span><span class='s'>ON</span></div>
</div></div>

<div class='dr'><div class='dh' data-f='camera'>CAMERA<i>&#x25B8;</i></div><div class='db' id='camera'>
 <div class='sw' id='all'><span class='n'>Automatic focus, exposure and white balance</span><span class='s'>ON</span></div>
 <div class='sw' id='so'><span class='n'>Image stabilization<i class='tip' data-tip='Off on a tripod or mount saves processing and heat. Changing it restarts the stream once.'>i</i></span><span class='s'>ON</span></div>
 <div class='sw' id='fo'><span class='n'>Focus once and lock<i class='tip' data-tip='Off: autofocus once and lock the lens there. On (locked, or held by the power-saving hold): press to go back to continuous autofocus.'>i</i></span><span class='s'>OFF</span></div>
 <div class='fld sl' id='fsl' style='margin-top:4px'><div class='t'><b>Focus</b><span class='seg' id='fseg'><span>Auto</span><span>Manual</span></span></div><div class='val'><em id='fv'>-</em><small id='fn'></small></div><input type='range' id='fs' min='0' max='100' step='1'></div>
 <div class='fld sl' id='esl'><div class='t'><b>Exposure</b><span class='seg' id='eseg'><span>Auto</span><span>Manual</span></span></div><div class='val'><em id='ev'>-</em><small id='en'></small></div><input type='range' id='es' min='0' max='100' step='0.5'></div>
 <div class='fld sl' id='wsl'><div class='t'><b>White balance</b><span class='seg' id='wseg'><span>Auto</span><span>Manual</span></span></div><div class='val'><em id='wv'>-</em><small id='wn'></small></div><input type='range' id='ws' min='2000' max='8000' step='100'></div>
</div></div>

<div class='dr'><div class='dh' data-f='power'>POWER<i>&#x25B8;</i></div><div class='db' id='power'>
 <div class='sw' data-k='heatprotect' id='hp'><span class='n'>Heat protection<i class='tip' data-tip='When charging pauses on a good charger the phone is hot: 1080p / 6 Mbps, screensaver on, stabilization off, flashlight locked, until it charges normally again. Off: nothing is changed, and on a weak charger the battery drains and the phone dies.'>i</i></span><span class='s'>ON</span></div>
 <div class='bad' id='ta2' style='display:none'></div>
 <div class='sw' data-k='keeppreview' id='kp'><span class='n'>Preview on always<i class='tip' data-tip='Off by default. On: the preview runs even behind the screensaver, so a tap shows the picture at once. Costs CPU and heat; heat protection switches it off.'>i</i></span><span class='s'>OFF</span></div>
 <div class='fld pk' id='usel' data-k='unplug'><b>When unplugged, stop streaming after<i class='tip' data-tip='A countdown shows on the phone when it goes on battery while streaming; when it ends the stream stops. It starts again when the power returns.'>i</i></b><div class='pv'><span>-</span><i>&#x25BE;</i></div><div class='list'></div></div>
</div></div>

<div class='dr'><div class='dh' data-f='net'>NETWORK AND LOGIN<i>&#x25B8;</i></div><div class='db' id='net'>
 <div class='sw' data-k='wifivideo' id='wv2'><span class='n'>Video over Wi-Fi<i class='tip' data-tip='The video for viewers that are not on the USB cable. The stream has no login (OBS and VLC cannot send one), so while it is on anyone on your network can watch. Off: only the USB cable gets video.'>i</i></span><span class='s'>ON</span></div>
 <div class='fld'><b>Wi-Fi video</b><code id='wvaddr'></code></div>
 <div id='wifivideowarn'></div>
 <div class='sw on dis' id='web'><span class='n'>Web page (settings and status)<i class='tip' data-tip='This page and /status. It can only be switched off on the phone (the page would cut itself off). The video is never affected.'>i</i></span><span class='s'>ON</span></div>
 <div class='fld'><b>Web page</b><code id='webaddr'></code></div>
 <div class='sw' data-k='logpage' id='lgo'><span class='n'>Log page (<a href='/log' target='_blank'>/log</a>)<i class='tip' data-tip='The app log in a browser, also for tools\Get-Phone-Log.cmd. Off by default.'>i</i></span><span class='s'>OFF</span></div>
 <div class='sw' data-k='statspage' id='sgo'><span class='n'>Stats page (<a href='/stats' target='_blank'>/stats</a>)<i class='tip' data-tip='Graphs of the last 12 hours (frame rate, bitrate, latency, battery, power, CPU, memory, heat), sampled every 30 s while on.'>i</i></span><span class='s'>OFF</span></div>
 <div class='fld'><b>Login (for changes)</b><div id='lmsg' style='font-size:12px;margin-top:3px'></div>
  <input id='lc' type='password' placeholder='Current password' autocomplete='current-password'><input id='lu' placeholder='User name (admin)' autocomplete='username'><input id='lp' type='password' placeholder='New password' autocomplete='new-password'><input id='lp2' type='password' placeholder='Repeat password' autocomplete='new-password'>
  <button id='lb'>Save login</button><button id='lrm' style='display:none'>Reset to admin / admin</button></div>
</div></div>

<div class='dr'><div class='dh' data-f='viewer'>VIEWER SETUP<i>&#x25B8;</i></div><div class='db' id='viewer'>
 <div style='font-size:11px;color:#7fae86;margin:0 2px 6px;line-height:1.4'>Which address: USB addresses (127.0.0.1) work only on the PC the phone is plugged into; Wi-Fi addresses from any device on the same network. 4K at Auto bitrate needs about 37 Mbps: use the USB cable, or 1080p over Wi-Fi. The video is raw H.264 with no container, so players must be told the format, as below.</div>
 <div class='fld'><b>Video addresses</b><code id='vaddr'></code></div>
 <div class='fld'><b>OBS Studio (as a webcam)</b><code id='vobs'></code></div>
 <div class='fld'><b>VLC</b><code id='vvlc'></code></div>
 <div class='fld'><b>ffplay (quick test)</b><code id='vffp'></code></div>
</div></div>

<div class='dr'><div class='dh' data-f='details'>DETAILS AND LOG<i>&#x25B8;</i></div><div class='db' id='details'>
 <div class='fld'><b>Now</b><code id='now'></code></div>
 <div class='fld'><b>Log (last lines)</b><div class='lg' id='logtail'>-</div></div>
</div></div>
<div id='msg'></div><div id='tipbox'></div>
<script>
var st=null,touched=0,t=null;
function $(i){return document.getElementById(i)}
function esc(t){return String(t==null?'':t).replace(/[&<>""']/g,function(c){return '&#'+c.charCodeAt(0)+';'})}
function e2s(ms){var a=st.expMin,b=st.expMax;return b>a?100*Math.log(ms/a)/Math.log(b/a):0}
function s2e(v){var a=st.expMin,b=st.expMax;return a*Math.pow(b/a,v/100)}
function sw(id,on,dis,txt){var e=$(id);e.className='sw'+(on?' on':'')+(dis?' dis':'');e.lastElementChild.textContent=txt||(on?'ON':'OFF')}
function bar(id,on,dis,txt){var e=$(id);e.className='bar'+(on?' on':'')+(dis?' dis':'');e.lastElementChild.textContent=txt}
function svc(id,cls,addr,note){$(id).className='svc'+(cls?' '+cls:'');$(id+'a').textContent=addr;$(id+'n').textContent=note||''}
function seg(id,auto){var s=$(id).children;s[0].className=auto?'a':'';s[1].className=auto?'':'a'}
function meter(p,cls){p=Math.max(0,Math.min(100,p||0));return ""<div class='meter ""+(cls||'')+""'><div style='width:""+p+""%'></div></div>""}
function tile(l,v,w,g,tip){return ""<div class='tile""+(w?' warn':'')+""'""+(tip?"" data-tip='""+esc(tip)+""'"":'')+'><b>'+esc(l)+'</b>'+(v===''?'':'<span>'+esc(v)+'</span>')+(g||'')+'</div>'}
function badge(t,c){return ""<div class='badge ""+(c||'')+""'>""+esc(t)+'</div>'}
function notice(t,bad,btn){return ""<div class='notice""+(bad?' bad':'')+""'><span class='ni'>!</span><span>""+esc(t)+'</span>'+(btn||'')+'</div>'}
// a picker tile: the current value on the tile, the themed list below it while open; a tap on a row sends the key (index-based)
function pick(id,vals,cur,dis,txt){
 var e=$(id),l=e.querySelector('.list'),key=vals.join('|')+'#'+cur;
 txt=txt||function(v){return v};
 e.querySelector('.pv span').textContent=cur>=0&&cur<vals.length?txt(vals[cur]):'-';
 e.className='fld pk'+(e.className.indexOf('open')>=0?' open':'')+(dis?' dis':'');
 if(l.dataset.k===key)return;l.dataset.k=key;l.innerHTML='';
 vals.forEach(function(v,i){var o=document.createElement('div');o.textContent=txt(v);if(i===cur)o.className='a';
  o.onclick=function(ev){ev.stopPropagation();e.className='fld pk';var k=e.getAttribute('data-k');send(k+'='+(k==='lens'?v.toLowerCase():i),k==='unplug'?'':'Applying - the stream restarts for a moment...')};l.appendChild(o)})
}
document.addEventListener('click',function(ev){var ps=document.querySelectorAll('.pk');for(var i=0;i<ps.length;i++){var p=ps[i];if(p.contains(ev.target)){if(ev.target.className==='tip')return;if(p.className.indexOf('dis')>=0){locked();return}p.className=p.className.indexOf('open')>=0?'fld pk':'fld pk open'}else p.className=p.className.replace(' open','')}});
function render(){
 if(!st||Date.now()-touched<2500)return;
 var w=st.canWrite||st.loginSet,s=st.streaming,p=st.protect; // with a login set, a change makes the browser ask for it
 if(st.version)$('ver').textContent='v'+st.version;
 var fm=/^(\d+) x (\d+) @ (\d+)/.exec(st.format||''),res=fm?(fm[2]>=2160?'4K':fm[2]+'p')+fm[3]+(s&&st.safeMode?' SAFE':''):'-';
 $('srow').className='srow'+(s?' on':'')+(p&&s?' hot':'')+(w?'':' busy');
 $('state').textContent=p&&s?'HEAT PROTECTION':s?'STREAMING':'START STREAM';$('fmt').textContent=res;
 $('tfps').textContent=s?Math.round(st.fps):'-';$('tmbps').textContent=s?(+st.mbps).toFixed(1):'-';$('tview').textContent=s?st.viewers:'-';
 var kt=document.querySelectorAll('.tile.k');for(var i=0;i<kt.length;i++)kt[i].className='tile k'+(s?'':' dim');
 var cp=st.cpuPhone==null?0:Math.round(st.cpuPhone),ca=st.cpuApp==null?0:Math.round(st.cpuApp),ml=st.memoryLimitMB||1536,mp=100*(st.memoryMB||0)/ml;
 var bp=st.battery==null?0:st.battery,bcls=st.batteryFalling||bp<20?'low':bp<40?'mid':'';
 var wb=st.wifiBars==null?0:st.wifiBars,bars=""<div class='bars""+(wb<2?' weak':'')+""'>"";
 for(var k=1;k<=4;k++)bars+=""<i class='""+(k<=wb?'on':'')+""' style='height:""+(2+k*3)+""px'></i>"";bars+='</div>';
 var lat=s&&st.latencyMs!=null&&st.latencyMs>=0?st.latencyMs:null,charging=st.batteryStatus=='Charging',pw=null,pl=charging?'Into battery':'Battery draw';
 if(!charging&&st.drawWatts!=null)pw=Math.abs(st.drawWatts);else if(st.netWatts!=null)pw=charging?st.netWatts:Math.abs(st.netWatts);
 var pv=pw==null?'measuring':(charging&&pw>=0?'+':'')+pw.toFixed(1)+' W';
 var pbar=pw==null?'':meter(100*Math.abs(pw)/15,charging?(pw<0?'low':pw<1?'mid':''):(pw>8?'low':'mid'));
 var eng=st.safeMode?'Safe mode':s?(st.engine=='low-latency'?'Fast':st.engine):'Ready',hl=st.heatLevel||0;
 $('tiles').innerHTML=tile('Battery',st.battery==null?'n/a':st.battery+'% '+(charging?'chg':String(st.batteryStatus).toLowerCase()),st.batteryFalling,meter(bp,bcls))
  +tile('Wi-Fi',st.wifiBars==null?'off':wb+' of 4',st.wifiBars!=null&&wb<2,bars)
  +tile('Temp','',false,badge(st.heatBadge||'n/a',hl==2?'bad':hl==1?'mid':''),'Windows gives apps no temperature. Hot means charging has paused on a good charger for 20 s.')
  +tile('CPU phone',cp+'%',false,meter(cp,cp>70?'mid':''))
  +tile('CPU app',ca+'%',false,meter(ca,ca>50?'mid':''))
  +tile('Memory',Math.round(st.memoryMB||0)+' MB',mp>80,meter(mp,mp>80?'low':mp>60?'mid':''),'App memory, of '+Math.round(ml)+' MB allowed.')
  +tile('Engine','',false,badge(eng,eng=='Fast'||eng=='Ready'?'':'mid'))
  +tile('Latency',lat==null?'-':lat+' ms',lat!=null&&lat>250,meter(lat==null?0:lat/3,lat>250?'low':lat>150?'mid':''),'Sensor to encoded frame inside the phone. USB and OBS add their own delay.')
  +tile(pl,pv,st.batteryFalling,pbar,(st.powerText||'')+' Bar scale: 0-15 W.');
 var nh='';
 if(st.alert)nh+=notice(st.alert,true,st.safeMode&&w?""<button id='engine'>Return to normal</button>"":'');
 if(st.defaultLogin)nh+=""<div class='notice'><span class='ni'>!</span><span>Default login <code>admin / admin</code> in use. Change it under Network and login.</span></div>"";
 if(st.chargerNotice)nh+=notice(st.chargerNotice,false);
 if(st.unplugLeftSec!=null)nh+=notice('The phone is on battery: streaming stops in '+Math.round(st.unplugLeftSec)+' s.',false,w?""<button id='keep'>Keep streaming</button>"":'');
 $('warn').innerHTML=nh;
 if($('engine'))$('engine').onclick=function(){send('engine=normal','Switching engine...')};
 if($('keep'))$('keep').onclick=function(){send('keepstreaming=1')};
 $('ro').style.display=st.canWrite?'none':'flex';$('rotext').textContent='View only. Changes need the login'+(st.defaultLogin?' (default admin / admin)':'')+'.';
 // service rows, as on the phone
 var ip=st.wifi||'',usb='127.0.0.1:'+st.usbPort,seen=st.usbSeen,cable=st.usbCable,un=seen?'':cable?'cable?':'no USB',uc=seen?'on':cable?'q':'',wvo=st.wifiVideoOn;
 var vt=function(n){return n>0?n+(n==1?' viewer':' viewers'):''};
 svc('rw',s&&wvo&&ip?'on':'',ip?'tcp://'+ip+':'+st.wifiPort:'',!ip?'no Wi-Fi':!wvo?'off':!s?'stopped':vt(st.viewersWifi));
 svc('ru',s?uc:'','tcp://'+usb,!s?'stopped':st.viewersUsb>0?vt(st.viewersUsb):un);
 svc('ww',ip?'on':'',ip?'http://'+ip+':'+st.logPort+'/settings':'',ip?'':'no Wi-Fi');
 svc('wu',uc,'http://'+usb+'/settings',un);
 $('wvaddr').textContent=ip?'tcp://'+ip+':'+st.wifiPort+(wvo?'':'  (off)'):'(not on Wi-Fi)';
 $('wifivideowarn').innerHTML=wvo?notice('Unauthenticated network video: anyone on this Wi-Fi can watch the camera. Switch off if you only use the USB cable.',false):'';
 $('webaddr').textContent='http://'+usb+'/settings (USB)'+(ip?'\nhttp://'+ip+':'+st.logPort+'/settings (Wi-Fi)':'');
 // the three buttons
 var pvOn=st.previewing||st.preview||st.keepPreview;
 bar('bpv',pvOn,!w,pvOn?'ON':'OFF');
 bar('bss',st.powerSave,!w||p,!st.powerSave?'OFF':p?'ON · HEAT PROTECTION':s?'ON':'ON · WHILE STREAMING');
 bar('btt',st.torchOn,!w||p||!st.torchSupported||!s,st.torchOn?'ON · '+st.torchLeftSec+' s':p?'OFF · HEAT PROTECTION':!st.torchSupported?'OFF · NOT ON THIS CAMERA':!s?'OFF · STREAM FIRST':'OFF');
 // video
 $('ta3').style.display=p?'block':'none';
 pick('lsel',['Rear','Front'],st.front?1:0,!w);
 pick('fsel',st.formats,st.formatIndex,!w||p);
 pick('bsel',st.bitrates,st.bitrateIndex,!w||p,function(b){return b=='Auto'?'auto':b});
 pick('ksel',st.keyframes,st.keyframeIndex,!w);
 pick('usel',st.unplugChoices,st.unplugIndex,!w);
 sw('ast',st.autoStart,!w);
 // camera
 sw('all',st.focusAuto&&st.exposureAuto&&st.wbAuto,!w);
 sw('so',!st.saveOis,!w||p||!st.oisSupported,!st.oisSupported?'NOT ON THIS CAMERA':!st.saveOis?'ON':'OFF');
 var fon=st.focusHeld||st.focusLocked;sw('fo',fon,!w||!st.focusSupported,st.focusHeld?'ON · HELD':fon?'ON · LOCKED':'OFF');
 seg('fseg',st.focusAuto);seg('eseg',st.exposureAuto);seg('wseg',st.wbAuto);
 $('fsl').className='fld sl'+(st.focusAuto?' off':'');$('esl').className='fld sl'+(st.exposureAuto?' off':'');$('wsl').className='fld sl'+(st.wbAuto?' off':'');
 $('fs').value=st.focus;$('es').value=Math.max(0,Math.min(100,e2s(st.exposureMs)));$('ws').min=st.wbMin;$('ws').max=st.wbMax;$('ws').value=st.wb;
 $('fs').disabled=!w||!st.focusSupported||st.focusAuto;$('es').disabled=!w||!st.exposureSupported||st.exposureAuto;$('ws').disabled=!w||!st.wbSupported||st.wbAuto;
 labels();
 // power
 sw('hp',st.heatProtect,!w);
 var hot=!p&&!st.heatProtect&&hl>=2;
 $('ta2').textContent=p?'Heat protection: power saving forced on':'HOT and heat protection is off: nothing is done about it - the battery drains on a weak charger';$('ta2').style.display=p||hot?'block':'none';
 sw('kp',st.keepPreview,!w||p);
 // network
 sw('wv2',wvo,!w);sw('lgo',st.logOn,!w);sw('sgo',st.statsOn,!w);
 $('lmsg').textContent=st.defaultLogin?'Default login admin / admin: change it, or anyone on your network can change settings.':'The browser asks for this login before changes, over USB and Wi-Fi'+(st.user?' (user '+st.user+')':'')+'.';
 $('lrm').style.display=st.defaultLogin?'none':'block';
 if(w&&st.user&&!$('lu').value&&document.activeElement!==$('lu'))$('lu').value=st.user;$('lb').disabled=!w;
 $('now').textContent=st.camera||'';
 // viewer setup, from the live addresses
 var ua='tcp://'+usb,wa=ip&&wvo?'tcp://'+ip+':'+st.wifiPort:null;
 $('vaddr').textContent='USB    '+ua+(wa?'\nWi-Fi  '+wa:ip?'\nWi-Fi  (video over Wi-Fi is off)':'');
 $('vobs').textContent='Add a Media Source, untick Local File, then\nInput\n  '+ua+(wa?'\n  '+wa+'  (Wi-Fi)':'')+'\nInput format\n  h264\nFFmpeg options\n  fflags=nobuffer flags=low_delay analyzeduration=0 probesize=500000\nTick Restart playback when source becomes active. Then Start Virtual Camera and pick OBS Virtual Camera in your video app.';
 $('vvlc').textContent='Media > Open Network Stream, enter the address, tick Show more options and add to Edit Options:\n  :demux=h264\nOr: vlc --demux=h264 '+ua+(wa?'\n    vlc --demux=h264 '+wa:'');
 $('vffp').textContent='ffplay -fflags nobuffer -flags low_delay -f h264 '+ua;
}
function labels(){
 var iso=/ISO (\d+)/.exec(st.camera||''),isoT=iso?' · ISO '+iso[1]:'';
 $('fv').textContent=st.focusAuto?'auto':Math.round($('fs').value);
 $('fn').textContent=!st.focusSupported?'not adjustable':st.focusHeld?'held, lens parked':st.focusAuto?'continuous':st.focusLocked?'locked · 0 near, 100 far':'0 near, 100 far';
 var ms=s2e($('es').value),mt=ms.toFixed(ms<10?2:1);
 $('ev').textContent=st.exposureAuto?'auto':mt+' ms';
 $('en').textContent=!st.exposureSupported?'not adjustable':(st.exposureAuto?(+st.exposureMs).toFixed(st.exposureMs<10?2:1)+' ms (1/'+Math.round(1000/Math.max(0.001,st.exposureMs))+' s)':'1/'+Math.round(1000/ms)+' s')+isoT+(st.exposureAuto?' now':'');
 $('wv').textContent=st.wbAuto?'auto':$('ws').value+' K';
 $('wn').textContent=!st.wbSupported?'not adjustable':st.wbAuto?st.wb+' K now':'';
}
// view only (no login possible): any tap on a control goes to the login page instead of doing nothing
function locked(){if(st&&(st.canWrite||st.loginSet))return false;$('msg').textContent='View only - set a login on the phone to change settings';return true}
function send(body,note){
 if(locked())return;
 touched=Date.now();if(note)$('msg').textContent=note;
 fetch('/settings/set',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded','X-Requested-With':'W10MWebCam'},body:body})
 .then(function(r){if(r.status==401){location.href='/login';throw new Error('Login needed')}return r.ok?r.json():r.text().then(function(x){throw new Error(x)})})
 .then(function(j){st=j;touched=0;render();if(j.result)$('msg').textContent=j.result})
 .catch(function(e){touched=0;$('msg').textContent=e.message})
}
function load(){
 fetch('/settings/state',{cache:'no-store'}).then(function(r){return r.json()}).then(function(j){
  if(st&&st.version&&j.version&&j.version!==st.version){location.reload();return}
  st=j;render()}).catch(function(){$('tiles').innerHTML=notice('The phone is not answering. Is the app open and the cable plugged in?',true)});
 if($('details').style.display!=='none')fetch('/log').then(function(r){return r.ok?r.text():''}).then(function(x){if(x){var l=x.trim().split('\n');$('logtail').textContent=l.slice(-25).join('\n')}}).catch(function(){});
}
function later(body){clearTimeout(t);t=setTimeout(function(){send(body)},400)}
function onoff(b){return b?'on':'off'}
$('loginb').onclick=function(){location.href='/login'};
$('srow').onclick=function(){if(!st||st.canWrite||st.loginSet)send('stream='+(st&&st.streaming?'stop':'start'),st&&st.streaming?'Stopping...':'Starting...')};
$('bpv').onclick=function(){var on=st&&(st.previewing||st.preview||st.keepPreview);send('preview='+(on?'off':'on'))};
$('bss').onclick=function(){send('powersave='+(st&&st.powerSave?'off':'on'))};
$('btt').onclick=function(){send('torch='+(st&&st.torchOn?'off':'on'))};
$('so').onclick=function(e){if(e.target.className==='tip')return;send('saveois='+(st&&st.saveOis?'off':'on'),'Applying - the stream restarts for a moment...')};
$('fo').onclick=function(e){if(e.target.className==='tip')return;if(st&&(st.focusHeld||st.focusLocked)){send('focus=auto')}else{send('focus=once','Focusing...')}};
$('all').onclick=function(){var a=st.focusAuto&&st.exposureAuto&&st.wbAuto;send('camera='+(a?'manual':'auto'))};
$('fseg').onclick=function(){send('focus='+(st.focusAuto?'manual':'auto'))};
$('eseg').onclick=function(){send('exposure='+(st.exposureAuto?'manual':'auto'))};
$('wseg').onclick=function(){send('wb='+(st.wbAuto?'manual':'auto'))};
$('fs').oninput=function(){touched=Date.now();labels();later('focus='+this.value)};
$('es').oninput=function(){touched=Date.now();labels();later('exposure='+s2e(this.value).toFixed(3))};
$('ws').oninput=function(){touched=Date.now();labels();later('wb='+this.value)};
// every switch row: data-k is the config key; the row's current state decides on/off
var rows=document.querySelectorAll('.sw[data-k]');
for(var i=0;i<rows.length;i++)rows[i].onclick=function(e){
 if(e.target.className==='tip'||e.target.tagName==='A')return;
 var on=(' '+this.className+' ').indexOf(' on ')>=0,k=this.getAttribute('data-k');
 if(k==='heatprotect'&&on&&!confirm('Turn heat protection off? A hot phone then keeps full video and the screen, and on a weak charger the battery drains until the phone dies.'))return;
 send(k+'='+(on?'off':'on'),k==='wifivideo'&&on?'Switching off video over Wi-Fi...':'')};
$('lb').onclick=function(){
 var u=$('lu').value.trim(),p=$('lp').value;
 if(p!==$('lp2').value){$('msg').textContent='The two passwords differ';return}
 if(p.length<8){$('msg').textContent='The password needs at least 8 characters';return}
 if(!$('lc').value){$('msg').textContent='Enter the current password too';return}
 var c=$('lc').value;$('lc').value=$('lp').value=$('lp2').value='';
 send('current='+encodeURIComponent(c)+'&user='+encodeURIComponent(u||'admin')+'&password='+encodeURIComponent(p));
 $('msg').textContent='Changing the login - if it worked, sign in again with the new login'};
$('lrm').onclick=function(){if(!$('lc').value){$('msg').textContent='Enter the current password too';return}
 var c=$('lc').value;$('lc').value=$('lp').value=$('lp2').value='';send('current='+encodeURIComponent(c)+'&password=','Resetting the login...')};
// drawers: the header band opens and closes the body; remembered in this browser (closed by default, as on the phone)
(function(){
 var saved={};try{saved=JSON.parse(localStorage.getItem('w10m-fold2')||'{}')}catch(e){}
 var hs=document.querySelectorAll('.dh');
 for(var i=0;i<hs.length;i++)(function(h){
  var name=h.getAttribute('data-f'),body=$(name);
  function show(open){body.style.display=open?'':'none';h.className='dh'+(open?'':' c');h.lastElementChild.textContent=open?'▾':'▸';if(open&&name==='details')load()}
  show(saved[name]===true);
  h.onclick=function(){var open=body.style.display==='none';show(open);saved[name]=open;try{localStorage.setItem('w10m-fold2',JSON.stringify(saved))}catch(e){}}
 })(hs[i]);
})();
// hover (or tap) anything with data-tip to see its note
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
load();setInterval(load,3000);
</script></body></html>
";
    }
}
