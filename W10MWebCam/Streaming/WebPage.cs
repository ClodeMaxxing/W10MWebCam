namespace W10MWebCam.Streaming
{
    /// <summary>
    /// The settings page (http://127.0.0.1:8020/settings over USB, http://PHONE-IP:5001/settings over Wi-Fi, view only).
    /// Plain HTML and a little script; it polls /settings/state every 2 s and posts changes to /settings/set.
    /// The page source avoids double quotes so it can live in a C# verbatim string.
    /// </summary>
    internal static class WebPage
    {
        public const string Html = @"<!doctype html>
<html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>W10MWebCam</title>
<style>
:root{--bg:#0d0f12;--card:#161a20;--line:#262c35;--text:#e6e9ee;--dim:#8b95a3;--acc:#7fb2e5;--ok:#4cc38a;--warn:#e8c547;--bad:#ef6b6b}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 'Segoe UI',system-ui,sans-serif}
main{max-width:720px;margin:0 auto;padding:16px}
header{display:flex;align-items:center;gap:10px;margin-bottom:12px}
header h1{font-size:20px;margin:0;font-weight:600}
#ver{color:var(--dim);font-size:13px}
.dot{width:10px;height:10px;border-radius:50%;background:#555;flex:none}
.dot.on{background:var(--ok);box-shadow:0 0 8px var(--ok)}.dot.hot{background:var(--bad);box-shadow:0 0 8px var(--bad)}
#state{margin-left:auto;font-size:13px;color:var(--dim)}
section{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px 14px;margin:10px 0}
h2{font-size:12px;letter-spacing:1px;text-transform:uppercase;color:var(--acc);margin:0 0 8px;font-weight:600}
.big{font:600 26px Consolas,'Cascadia Mono',monospace}.big small{font-size:12px;color:var(--dim);margin:0 14px 0 4px;font-weight:400}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:8px;margin-top:8px}
.tile{background:#11151a;border:1px solid var(--line);border-radius:6px;padding:6px 8px;min-width:0}
.tile b{display:block;font-size:10px;letter-spacing:1px;text-transform:uppercase;color:var(--dim);font-weight:600}
.tile span{display:block;font:13px Consolas,'Cascadia Mono',monospace;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.tile.warn span{color:var(--bad)}
.notice{border-left:3px solid var(--warn);background:#2a2610;color:#f0e0a0;padding:8px 10px;border-radius:4px;margin-top:8px;font-size:13px}
.row{display:flex;align-items:center;gap:8px;flex-wrap:wrap;margin:8px 0}
.row label.grow{flex:1;min-width:150px}
select,button{font:inherit;color:var(--text);background:#11151a;border:1px solid var(--line);border-radius:6px;padding:6px 10px}
select{min-width:150px}
button{cursor:pointer}button:hover:not(:disabled){border-color:var(--acc)}
button.primary{background:#1d3550;border-color:#2d5580}
button:disabled,select:disabled,input:disabled{opacity:.4;cursor:default}
input[type=range]{width:100%;accent-color:var(--acc)}input[type=checkbox]{accent-color:var(--acc);width:16px;height:16px;vertical-align:-3px}
.slider{margin:10px 0}.slider .head{display:flex;justify-content:space-between;align-items:center}
.note{color:var(--dim);font-size:12px}
code{font:13px Consolas,'Cascadia Mono',monospace;background:#0b0e12;border:1px solid var(--line);border-radius:4px;padding:1px 6px;user-select:all}
#msg{min-height:20px;color:var(--acc);font-size:13px;margin-top:6px}
#ro{display:none;color:var(--warn)}
.cols{display:grid;grid-template-columns:1fr 1fr;gap:0 18px}
@media (max-width:560px){.cols{grid-template-columns:1fr}.big{font-size:22px}}
</style></head><body><main>
<header><span id='dot' class='dot'></span><h1>W10MWebCam</h1><span id='ver'></span><span id='state'>connecting...</span></header>
<div id='ro' class='notice'>View only: settings can be changed only from the PC the phone is plugged into, at <code>http://127.0.0.1:8020/settings</code>.</div>

<section><h2>Status</h2>
<div class='big' id='big'></div>
<div class='grid' id='tiles'></div>
<div id='notice'></div>
<div class='row' id='saferow' style='display:none'><span class='note'>Safe mode: the standard (MP4) engine is in use for this launch.</span><button id='engine'>Return to the low-latency engine</button></div>
<div class='row' id='unplugrow' style='display:none'><span class='notice' id='unplugtext' style='margin:0'></span><button id='keep'>Keep streaming</button></div>
</section>

<section><h2>Video</h2>
<div class='row'><button id='stream' class='primary'>Start streaming</button><span class='note' id='streamnote'></span></div>
<div class='cols'>
<div class='row'><label class='grow'>Resolution<br><select id='format'></select></label></div>
<div class='row'><label class='grow'>Bitrate<br><select id='bitrate'></select></label></div>
<div class='row'><label class='grow'>Keyframe every<br><select id='keyframe'></select></label></div>
</div>
<div class='note'>Changing these restarts the stream for a moment; OBS reconnects by itself.</div>
</section>

<section><h2>Camera</h2>
<div class='note' id='camnow'></div>
<div class='row'><label><input type='checkbox' id='camauto'> Automatic focus, exposure and white balance</label></div>
<div class='slider'><div class='head'><span id='fl'>Focus</span><label><input type='checkbox' id='fa'> Auto</label></div>
<input type='range' id='fs' min='0' max='100' step='1'>
<div class='row'><button id='fonce'>Focus once and lock</button><label><input type='checkbox' id='sf'> Hold focus once it has settled</label></div></div>
<div class='slider'><div class='head'><span id='el'>Exposure</span><label><input type='checkbox' id='ea'> Auto</label></div>
<input type='range' id='es' min='0' max='100' step='0.5'></div>
<div class='slider'><div class='head'><span id='wl'>White balance</span><label><input type='checkbox' id='wa'> Auto</label></div>
<input type='range' id='ws' min='2000' max='8000' step='100'></div>
<div class='row'><label><input type='checkbox' id='torch'> Flashlight</label><span class='note' id='torchnote'></span></div>
<div class='row'><label><input type='checkbox' id='ois'> Stabilization off (fixed mount: less processing and heat; restarts the stream)</label></div>
</section>

<section><h2>Phone and power</h2>
<div class='row'><label><input type='checkbox' id='powersave'> Black out the phone screen while streaming (saves battery, lets it charge)</label></div>
<div class='row'><label><input type='checkbox' id='autostart'> Start streaming when the app opens</label></div>
<div class='row'><label><input type='checkbox' id='preview'> Preview on the phone (applies when the stream is stopped or restarted)</label></div>
<div class='row'><label><input type='checkbox' id='heatauto'> Lower to 1080p / 6 Mbps automatically if the phone overheats</label></div>
<div class='row'><label>When unplugged, stop streaming after <select id='unplug'></select></label></div>
</section>

<section><h2>Connect</h2>
<div class='note'>OBS: Media Source, untick Local File, Input <code>tcp://127.0.0.1:8020</code>, Input Format <code>h264</code>,
FFmpeg options <code>fflags=nobuffer flags=low_delay analyzeduration=0 probesize=500000</code>.</div>
<div class='note' id='wifi' style='margin-top:6px'></div>
</section>
<div id='msg'></div>

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
function tile(label,value,warn){return '<div class=\'tile'+(warn?' warn':'')+'\'><b>'+esc(label)+'</b><span>'+esc(value)+'</span></div>'}
function render(){
 if(!st)return;
 var w=st.canWrite;
 $('ro').style.display=w?'none':'block';
 $('ver').textContent='v'+st.version;
 $('dot').className='dot'+(st.protect?' hot':st.streaming?' on':'');
 $('state').textContent=st.protect?'Heat protection':st.streaming?'Streaming '+st.format:'Stopped';
 $('big').innerHTML=st.streaming?esc(Math.round(st.fps))+'<small>fps</small>'+esc(fmt(st.mbps,1))+'<small>Mbps</small>'+esc(st.viewers)+'<small>viewer'+(st.viewers==1?'':'s')+'</small>':'<small>Not streaming</small>';
 var bat=st.battery==null?'n/a':st.battery+' % '+String(st.batteryStatus).toLowerCase();
 var pw=st.netWatts==null?'measuring':(st.netWatts>=0?'+':'')+fmt(st.netWatts,1)+' W';
 var mem=st.memoryMB==null?'n/a':Math.round(st.memoryMB)+' MB';
 $('tiles').innerHTML=tile('Engine',st.safeMode?'standard (safe mode)':(st.engine||'-'),st.safeMode)
  +tile('Sensor to encoded',st.latencyMs==null?'n/a':st.latencyMs+' ms',st.latencyMs>250)
  +tile('Keyframe every',st.keyframeSec?fmt(st.keyframeSec,1)+' s':'n/a')
  +tile('Battery',bat,st.battery!=null&&st.battery<20)
  +tile('Battery flow',pw,st.netWatts!=null&&st.netWatts<-0.3)
  +tile('Charger',String(st.charger).toLowerCase(),st.charger=='Inadequate')
  +tile('Heat',st.heat,st.heat!='Healthy')
  +tile('CPU phone / app',(st.cpuPhone==null?'?':Math.round(st.cpuPhone))+' % / '+(st.cpuApp==null?'?':Math.round(st.cpuApp))+' %')
  +tile('App memory',mem);
 $('notice').innerHTML=st.notice?'<div class=\'notice\'>'+esc(st.notice)+'</div>':'';
 $('saferow').style.display=st.safeMode?'flex':'none';$('engine').disabled=!w;
 $('unplugrow').style.display=st.unplugLeftSec!=null?'flex':'none';
 $('unplugtext').textContent='The phone is on battery: streaming stops in '+Math.round(st.unplugLeftSec||0)+' s.';$('keep').disabled=!w;
 if(Date.now()<busyUntil)return; // don't overwrite what the person is changing
 $('stream').textContent=st.streaming?'Stop streaming':'Start streaming';$('stream').disabled=!w;
 $('streamnote').textContent=st.protect?'Heat protection has lowered the stream; your own settings come back when charging resumes.':'';
 fill($('format'),st.formats,st.formatIndex);fill($('bitrate'),st.bitrates,st.bitrateIndex);fill($('keyframe'),st.keyframes,st.keyframeIndex);
 fill($('unplug'),st.unplugChoices,st.unplugIndex);
 ['format','bitrate','keyframe','unplug'].forEach(function(i){$(i).disabled=!w});
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
 $('torchnote').textContent=!st.torchSupported?'not offered by this camera':st.torchOn?'off in '+st.torchLeftSec+' s':!st.streaming?'only while streaming':'switches off after 30 s (it heats the phone)';
 $('ois').checked=st.saveOis;$('ois').disabled=!w||!st.oisSupported;
 ['powersave','autostart','preview','heatauto'].forEach(function(i){$(i).checked=st[i=='powersave'?'powerSave':i=='autostart'?'autoStart':i=='heatauto'?'heatAuto':'preview'];$(i).disabled=!w});
 $('wifi').innerHTML=st.wifi?'Over Wi-Fi: <code>tcp://'+esc(st.wifi)+':'+st.wifiPort+'</code> (video), <code>http://'+esc(st.wifi)+':'+st.logPort+'/settings</code> (this page, view only).':'';
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
 .then(function(r){return r.ok?r.json():r.text().then(function(x){throw new Error(x)})})
 .then(function(j){st=j;busyUntil=0;render();if(j.result)$('msg').textContent=j.result})
 .catch(function(e){busyUntil=0;$('msg').textContent=e.message});
}
function load(){
 fetch('/settings/state',{cache:'no-store'}).then(function(r){return r.json()}).then(function(j){st=j;render()})
 .catch(function(){$('state').textContent='phone not reachable';$('dot').className='dot'});
}
function later(body){busyUntil=Date.now()+4000;clearTimeout(timer);timer=setTimeout(function(){send(body)},400)}
function onoff(b){return b?'on':'off'}
$('stream').onclick=function(){send('stream='+(st&&st.streaming?'stop':'start'),st&&st.streaming?'Stopping...':'Starting...')};
$('engine').onclick=function(){send('engine=normal','Switching engine...')};
$('keep').onclick=function(){send('keepstreaming=1')};
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
load();setInterval(load,2000);
</script></main></body></html>
";
    }
}
