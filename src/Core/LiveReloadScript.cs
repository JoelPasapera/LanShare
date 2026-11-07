namespace LanShare.Core
{
    /// <summary>
    /// Script de recarga automatica, inyectado junto a la sonda.
    ///
    /// Usa WebSocket y no Server-Sent Events a proposito: una conexion SSE
    /// cuenta dentro del limite de seis conexiones HTTP/1.1 por origen del
    /// navegador, y con seis pestanas del mismo sitio la septima se quedaba
    /// colgada esperando. Los WebSocket tienen un limite propio, mucho mayor.
    ///
    /// Protocolo: el servidor envia "reload" o "css" seguido de las rutas de
    /// las hojas cambiadas, una por linea. Si ninguna hoja de la pagina
    /// coincide (CSS importado o inyectado por JavaScript), recarga entera.
    /// Al cerrarse la conexion reintenta con espera creciente, salvo con el
    /// codigo 4000, que significa que la recarga se desactivo.
    ///
    /// No lleva comillas dobles para poder vivir en una cadena literal sin
    /// escapes.
    /// </summary>
    public static class LiveReloadScript
    {
        public const string Html =
@"<script>(function(){
if(!window.WebSocket||window.__lanshareLive)return;window.__lanshareLive=1;
var delay=1000;
function swapCss(paths){
var links=document.querySelectorAll('link[rel=stylesheet]'),n=0;
for(var i=0;i<links.length;i++){
var l=links[i],u,p;
try{u=new URL(l.href,location.href);}catch(e){continue;}
if(u.origin!==location.origin)continue;
try{p=decodeURIComponent(u.pathname);}catch(e){p=u.pathname;}
if(paths.indexOf(p)<0)continue;
u.searchParams.set('__lr',String(Date.now()));
var c=l.cloneNode(false);c.href=u.href;
c.onload=(function(o){return function(){if(o.parentNode)o.parentNode.removeChild(o);};})(l);
l.parentNode.insertBefore(c,l.nextSibling);n++;}
return n;}
function connect(){
var ws;
try{ws=new WebSocket((location.protocol==='https:'?'wss://':'ws://')+location.host+'/__events');}catch(e){return;}
ws.onopen=function(){delay=1000;};
ws.onmessage=function(e){
var lines=String(e.data).split('\n');
if(lines[0]==='css'&&swapCss(lines.slice(1))>0)return;
location.reload();};
ws.onclose=function(e){
if(e.code===4000)return;
setTimeout(connect,delay);delay=Math.min(delay*2,10000);};}
connect();})();</script>";
    }
}
