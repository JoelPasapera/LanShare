namespace ServidorWebPro.Identity
{
    /// <summary>
    /// Sonda inyectada antes de &lt;/body&gt; en las respuestas HTML.
    ///
    /// Existe porque las cabeceras HTTP no dicen casi nada del hardware. El
    /// navegador si lo sabe: WEBGL_debug_renderer_info devuelve el SoC exacto
    /// (Adreno 740, Mali-G715, Apple A17), que es la senal que mejor acota el
    /// modelo de un Android con User-Agent reducido.
    ///
    /// Se envia con Content-Type text/plain a proposito: application/json
    /// dispararia una peticion de verificacion previa si la pagina se sirviera
    /// desde otro origen.
    /// </summary>
    public static class ProbeScript
    {
        public const string Html =
@"<script>(function(){try{
var n=navigator,s=screen,cn=n.connection||{},gpu="""";
try{var cv=document.createElement(""canvas"");
var gl=cv.getContext(""webgl"")||cv.getContext(""experimental-webgl"");
if(gl){var dx=gl.getExtension(""WEBGL_debug_renderer_info"");
gpu=dx?(gl.getParameter(dx.UNMASKED_RENDERER_WEBGL)+"" | ""+gl.getParameter(dx.UNMASKED_VENDOR_WEBGL)):gl.getParameter(gl.RENDERER);}
}catch(e){}
var p={screen:s.width+""x""+s.height+"" @""+(window.devicePixelRatio||1)+""x ""+(s.colorDepth||""?"")+""bit"",
viewport:window.innerWidth+""x""+window.innerHeight,gpu:gpu,
cores:n.hardwareConcurrency||""?"",ram:n.deviceMemory||""?"",touch:n.maxTouchPoints||0,
platform:n.platform||"""",langs:(n.languages||[]).join("",""),
tz:(function(){try{return Intl.DateTimeFormat().resolvedOptions().timeZone}catch(e){return""""}})(),
net:(cn.effectiveType||"""")+(cn.downlink?"" ~""+cn.downlink+""Mbps"":"""")+(cn.rtt?"" rtt""+cn.rtt+""ms"":"""")};
function send(o){try{var x=new XMLHttpRequest();x.open(""POST"",""/__probe"",true);
x.setRequestHeader(""Content-Type"",""text/plain"");x.send(JSON.stringify(o));}catch(e){}}
if(n.userAgentData&&n.userAgentData.getHighEntropyValues){
n.userAgentData.getHighEntropyValues([""platformVersion"",""model"",""architecture"",""bitness""])
.then(function(h){p.hints=h;send(p);})[""catch""](function(){send(p);});}else{send(p);}
}catch(e){}})();</script>";
    }
}
