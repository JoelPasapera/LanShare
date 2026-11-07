# LanShare

Comparte una carpeta con tu teléfono, tu tablet o cualquier equipo de la red
local. Un solo ejecutable pequeño que corre en cualquier Windows 10 u 11 sin
instalar nada.

<!-- Sustituye por una captura real: docs/capturas/principal.png -->
<!-- ![Ventana principal](docs/capturas/principal.png) -->

## Qué hace

- Sirve **varias carpetas a la vez**, cada una en su propio puerto: un sitio por
  proyecto, todos desde una sola ventana y un solo registro.
- Cada sitio elige su alcance por separado: `localhost` o toda la red local.
- Genera un **código QR** de la dirección para abrirla en el móvil sin teclear la IP.
  Si el equipo tiene varias, propone primero la de la red real, no la de WSL o
  una máquina virtual.
- **Lista las carpetas** que no tienen archivo de inicio: ruta navegable, orden
  por nombre, tamaño o fecha, filtro por nombre y **descarga de la carpeta
  entera en ZIP**, generado mientras se envía.
- Cada sitio elige su **archivo de inicio** de entre los `.html` que hay en la
  carpeta, o se sirve como carpeta y lista siempre su contenido.
- **Comprime** HTML, CSS, JS, JSON y SVG con gzip (entre un 60 % y un 80 % menos),
  y guarda el resultado: cada archivo se comprime una sola vez hasta que cambia.
- **HTTPS en la red local**, con una autoridad de certificación propia del
  equipo que se instala en el móvil escaneando un QR: WebGPU, *service
  workers* o la cámara funcionan en el móvil igual que en `localhost`, y con el
  aislamiento entre orígenes también `SharedArrayBuffer` y WebAssembly con
  hilos. La autoridad solo puede avalar direcciones privadas y nombres
  `.local`. *(Opcional, por sitio)*
- **Recarga las páginas al guardar** en todos los dispositivos a la vez; si solo
  cambió una hoja de estilos, la sustituye en caliente sin recargar. *(Opcional)*
- **Consola remota:** los errores de JavaScript y los `console.log` de las
  páginas abiertas en el móvil aparecen en el registro del PC, con archivo y
  línea. *(Opcional, apagada de serie)*
- **Recibe archivos desde el móvil** en `/__subir`: fotos y capturas al PC sin
  cable ni cuentas, a su calidad original. *(Opcional, apagada de serie)*
- Vive en la **bandeja del sistema** y puede **iniciarse con Windows**, sirviendo
  los sitios marcados sin abrir el navegador.
- **Añade un sitio arrastrando su carpeta** a la ventana, con puerto libre y
  página de inicio ya propuestos, y se maneja con **atajos de teclado**.
- Soporta **descargas parciales** (`Range`), así que el vídeo admite búsqueda.
- **Identifica cada cliente** combinando DNS inverso, ARP, TTL por ICMP, mDNS,
  NetBIOS, SSDP, escaneo TCP de puertos con firma y una sonda JavaScript que
  reporta la GPU y la resolución real.

> **Aviso.** Para identificar a los clientes, el programa hace **sondeo activo**:
> abre conexiones TCP hacia 21 puertos del dispositivo que se conecta e inyecta
> un `<script>` en las páginas HTML que sirve. En tu propia red es tu decisión,
> pero conviene saberlo: en una red corporativa un escaneo de puertos suele
> disparar las alertas del sistema de detección de intrusos. Ambas cosas se
> desactivan desde *Configuración → Clientes*.

## Instalación

Descarga `LanShare.exe` de la sección
[Releases](https://github.com/JoelPasapera/LanShare/releases) y ejecútalo. No
requiere instalador ni dependencias: .NET Framework 4.8 ya viene con Windows.

Para el modo red local hace falta administrador (clic derecho → *Ejecutar como
administrador*, o el botón dentro de la aplicación).

## Uso diario

**Menú contextual de sitios.** Clic derecho en la tabla: abrir en el navegador,
abrir la carpeta, copiar la URL local o de red, ver el QR de ese sitio o el QR
de la página de subida.

**Editar con el servidor en marcha.** Añadir, editar, quitar o marcar sitios ya
no obliga a detener el servidor: el cambio se guarda y el servidor se reinicia
solo en un segundo, sin abrir pestañas (las páginas con recarga automática se
reconectan solas). Varios cambios seguidos se aplican de una vez, los
dispositivos identificados se conservan y un sitio recién añadido se abre en el
navegador. Lo que no cambia lo que se sirve (un sitio desmarcado, aceptar sin
tocar nada) no reinicia, porque cada reinicio corta las descargas en curso.
Desmarcar el último sitio simplemente detiene el servidor.

**HTTPS en el móvil.** Los navegadores reservan a las conexiones seguras buena
parte de lo moderno (WebGPU, *service workers*, cámara y micrófono,
portapapeles, `SharedArrayBuffer`), y `http://192.168.1.5` no lo es. Para
probarlo en el móvil:

1. *Editar sitio* → **Servir también por HTTPS**. Propone el puerto siguiente
   al HTTP (3000 → 3001); el HTTP sigue funcionando igual en el suyo.
2. Inicia el servidor **como administrador** la primera vez: hay que guardar el
   certificado del servidor en el almacén del equipo y asociarlo al puerto en
   `http.sys`. LanShare crea entonces su autoridad local (un segundo o dos).
3. Clic derecho en el sitio → **Código QR para instalar el certificado HTTPS en
   el móvil**. La página que se abre (`/__certificado`, por HTTP) descarga la
   autoridad y explica los pasos de Android y de iPhone; en iPhone hay que
   activarla además en *Ajustes → General → Información → Ajustes de confianza
   de certificados*.
4. Compara la **huella SHA-256** que muestra el móvil con la de
   *Configuración → HTTPS*. Si no coinciden, no la instales.
5. Abre la dirección `https://`: el QR del sitio la pone la primera.

Se hace una vez por móvil. El certificado del servidor dura 397 días, por
debajo de lo que admiten iOS y los navegadores, y se renueva solo al iniciar
como administrador cuando le quedan menos de 30 o cuando el equipo estrena una
dirección en la red local (las de WSL y las del conmutador por defecto de
Hyper-V no cuentan: cambian en cada arranque y ningún móvil llega a ellas),
siempre con la misma autoridad: el móvil no nota nada. La autoridad dura diez
años; cuando le queda menos de uno, LanShare crea otra y avisa para que la
instales de nuevo.

`SharedArrayBuffer`, y con él WebAssembly con hilos, pide además que la página
esté **aislada de otros orígenes**: márcalo en *Configuración → Desarrollo* y
LanShare enviará `Cross-Origin-Opener-Policy: same-origin` y
`Cross-Origin-Embedder-Policy: require-corp`. En el PC basta `localhost`, que
ya cuenta como seguro; en el móvil hace falta la dirección HTTPS. Con el
aislamiento, la página no puede cargar recursos de otros dominios que no lo
autoricen (CORS o `Cross-Origin-Resource-Policy`), por eso viene apagado.

En *Configuración → HTTPS* están también **Confiar en este PC**, para que
Chrome y Edge abran `https://localhost` sin aviso en el propio equipo, y
**Borrar los certificados de LanShare**, que lo quita todo de Windows.

**Altura de las cajas a tu gusto.** Pasa el cursor por el borde de arriba o de
abajo de la tabla de sitios, del estado, del registro o del detalle de un
cliente: aparece el cursor de redimensionar y, al arrastrar, la caja crece y
las de al lado se apartan. Bajo el registro, el borde es el de la propia
ventana. Doble clic en un borde devuelve las alturas originales, y el reparto
se recuerda entre sesiones.

**Atajos de teclado.**

| Tecla | Acción |
|---|---|
| `F5` | Iniciar o detener el servidor |
| `Ctrl+N` | Añadir un sitio |
| `Ctrl+,` | Configuración |
| `Ctrl+S` | Guardar el registro |
| `Intro` | Con un sitio seleccionado: abrirlo en el navegador (en marcha) o editarlo (parado) |
| `F2` / `Supr` | Editar / quitar el sitio seleccionado |
| `Ctrl+C` | Copiar la URL local del sitio seleccionado |

Los de la tabla solo actúan con el foco en ella, así que `Supr` y `Ctrl+C`
siguen funcionando como siempre en el registro.

**Añadir un sitio arrastrando.** Suelta una carpeta sobre la ventana y se abre
el diálogo con la carpeta, un puerto libre y la página de inicio ya puestos
(`index.html` si existe; si no, la única página que haya; con varias, el
listado). Si sueltas una página `.html`, se usa su carpeta abriendo esa página.
Funciona también con LanShare elevado: Windows bloquea el arrastre normal hacia
ventanas de administrador, así que en ese caso se recibe con `WM_DROPFILES`.

**Línea de comandos.**

| Opción | Efecto |
|---|---|
| `--start` | Inicia los sitios marcados al abrir, sin abrir pestañas |
| `--tray` | Arranca oculto en la bandeja del sistema |
| `--wait-instance` | Espera a que cierre la instancia anterior (uso interno al escalar) |

También valen con barra (`/start`) y sin distinguir mayúsculas.

**Una sola instancia.** Abrir LanShare cuando ya está abierto trae al frente la
ventana existente, aunque esté oculta en la bandeja o se ejecute como
administrador. Dos instancias se pisaban el puerto 5353 de mDNS y la
configuración.

**Iniciar con Windows** (Configuración → Inicio). Con administrador se registra
como tarea programada con privilegios elevados: arranca sin pedir permiso y los
sitios en red local funcionan. Sin administrador usa la clave `Run` del usuario
y arranca sin elevar. La tarea se registra por XML porque los valores por
defecto de `schtasks` no sirven para un servidor: no arranca con batería, se
detiene al desenchufar y se mata a los tres días.

**Recarga automática** (Configuración → Desarrollo). Un vigilante por sitio
agrupa los eventos del disco en ráfagas de 180 ms y decide una vez por lote:
solo CSS se cambia en caliente, cualquier otra cosa recarga. Ignora `.git`,
`node_modules` y los temporales de los editores. Va por WebSocket y no por
Server-Sent Events: una conexión SSE cuenta dentro del límite de seis por
origen del navegador, y con seis pestañas la séptima se quedaba colgada.

**Consola remota** (Configuración → Desarrollo). En el móvil no hay
herramientas de desarrollo, y la causa típica de una pantalla en blanco es un
error de JavaScript al arrancar. Con esta opción, cada página que piden los
demás dispositivos lleva al principio de `<head>` un script que reenvía al
registro los errores sin capturar, las promesas rechazadas, los recursos que no
cargan y las llamadas a `console.log`, `warn`, `error`, `info` y `debug`. Salen
como `JS ERROR`, `JS WARN` o `JS LOG`, con el archivo y la línea, en el filtro
del sitio. Los mensajes repetidos se agrupan (`x12`), se envían en lotes y cada
dispositivo tiene un ritmo máximo, para que una página con un error en bucle no
ahogue el registro. No se inyecta en las páginas que abres en el propio PC: ahí
ya tienes la consola del navegador.

**Listado de carpetas.** La ruta de arriba enlaza cada tramo, las columnas se
ordenan con un clic (las carpetas siempre delante) y a partir de ocho entradas
aparece un filtro por nombre. *Descargar la carpeta (.zip)* empaqueta la carpeta
con sus subcarpetas mientras se envía, sin archivos temporales. Sigue las
mismas reglas que el listado (nada oculto ni de sistema) y la misma contención
que una descarga normal: cada archivo y cada carpeta se validan por su ruta
real, así que un enlace o una unión que apunte fuera del sitio se queda fuera.
Si un archivo falla a mitad de lectura, el ZIP sigue siendo válido; si falla
otra cosa, la descarga se corta en seco para que el navegador no la dé por buena.

**Subida de archivos** (Configuración → Subida). Es la única función que
escribe en disco desde la red, así que todo lo que entra se trata como hostil:

- Solo se acepta desde la propia página: `Origin` y `Sec-Fetch-Site` se
  comprueban, porque un formulario multipart de otra web llegaría sin preguntar.
- Solo con un nombre de equipo que no se pueda suplantar desde Internet (una IP,
  `localhost`, el nombre del PC o un `.local`): así una web que haga apuntar su
  dominio a tu IP (*DNS rebinding*) tampoco puede subir nada.
- La carpeta de destino nunca puede estar dentro de un sitio servido.
- Los nombres se sanean: sin rutas, sin caracteres prohibidos, sin nombres de
  dispositivo (`CON`, `NUL`, `COM1`…), sin puntos finales y sin caracteres
  invisibles de dirección, que permitirían disfrazar `foto.exe` de `foto.jpg`.
- Nunca se sobrescribe: si existe `foto.jpg`, la nueva es `foto (2).jpg`.
- Cada archivo se escribe con un nombre temporal y solo recibe el definitivo si
  llega completo. El tamaño se limita mientras llega, no al final.
- Se deja siempre medio GB libre en el disco.

## Compilar desde el código

```
cd src
build.cmd
```

Usa el `csc.exe` que .NET Framework 4.8 incluye de serie. **No hace falta Visual
Studio, ni SDK, ni paquetes NuGet.** El resultado es `src/bin/LanShare.exe`.

Ese compilador es **C# 5**, por eso el código evita interpolación de cadenas,
operador `?.` y `nameof`. Si modificas algo, mantén esa restricción o perderás
la propiedad de compilar sin instalar nada.

También se incluye un `.csproj` por si prefieres Visual Studio; produce el mismo
binario.

## Pruebas

```
cd tests
build-tests.cmd
```

Compila y ejecuta la batería completa. Devuelve un código de salida distinto de
cero si algo falla, así que se puede encadenar con la compilación.

No usa ningún framework externo: el ejecutor, las aserciones y el árbol de
archivos temporal están en `tests/Framework/`, y todo se compila con el mismo
`csc` que trae Windows.

Las pruebas compilan los fuentes de la aplicación **junto** a los suyos, en
lugar de referenciar el `.exe` ya construido. Eso permite alcanzar también los
tipos internos (`DnsWire`, `NativePath`) sin tener que abrirlos al exterior.

Lo que conviene destacar:

- **`tests/Core/QrDecoder.cs`** es un decodificador de códigos QR independiente.
  Construye su propio campo finito, reconstruye por su cuenta el mapa de módulos
  de servicio, verifica los síndromes Reed-Solomon con matemática distinta a la
  del codificador y lee las dos copias de los bits de formato en el orden de
  ZXing. Por sí solo no basta: compartía con el codificador un error en los
  bits de formato, las pruebas pasaban y ningún móvil leía los códigos. Por eso
  se contrasta con **`QrReferenceCodes`**, códigos que leyó OpenCV, y se exige
  que LanShare genere exactamente esas matrices.
- Las pruebas marcadas **`REGRESION:`** cubren fallos que ya ocurrieron una vez.
- Las marcadas **`SEGURIDAD:`** cubren la contención de rutas, el escapado del
  listado de carpetas, los punteros circulares de DNS, el control de origen y
  de nombre de host de la subida y de la consola remota, y la neutralización de
  lo que llega de la red antes de escribirlo en el registro.
- **`ZipTests`** lee con `ZipArchive` de .NET, que no comparte código con el
  escritor, todo lo que escribe `ZipWriter`, incluido el modo ZIP64.
- **HTTPS:** las restricciones de nombre y el identificador de clave que
  LanShare escribe a mano en DER se comparan byte a byte con los de la
  biblioteca `cryptography` de Python; el lector DER se somete a cada corte y
  cada byte cambiado de una estructura real; y **`CertificateFactoryTests`**
  emite los certificados con claves efímeras y valida la cadena con el
  verificador de Windows, que tiene que rechazar un servidor con una IP o un
  dominio de Internet. No se toca ningún almacén de certificados.

## Estructura

| Carpeta | Responsabilidad |
|---|---|
| `src/Core/` | Servidor HTTP asíncrono multi-sitio, rutas, contención, MIME, rangos, compresión, caché, QR, recarga, consola remota, subida, ZIP |
| `src/Https/` | HTTPS local: autoridad de certificación, certificado del servidor, DER, restricciones de nombre, asociaciones en `http.sys` |
| `src/Identity/` | Sondeo de red, parseo de User-Agent, registro de clientes, fusión de señales |
| `src/Net/` | Direcciones LAN, firewall, elevación de privilegios, puertos libres |
| `src/Platform/` | Inicio con Windows: tarea programada y clave Run |
| `src/Ui/` | Formulario, diálogos, bandeja, atajos, arrastre de carpetas, cajas de altura ajustable, exportación CSV |
| `tests/` | Batería de pruebas, sin dependencias externas |
| `legacy/` | Versión 1 en PowerShell, congelada |

Las piezas de `Core` e `Identity` son funciones puras sin estado
(`RangeParser`, `UaParser`, `StaticFileResolver`, `DnsWire`, `VerdictEngine`,
`EntityTag`, `QrCode`, `HtmlInjector`, `ConsoleLine`, `UploadNaming`), lo que
permite probarlas sin levantar una ventana.

## Notas técnicas

**Concurrencia.** Cada petición es una máquina de estados asíncrona sobre el
thread pool de .NET. Mientras un archivo viaja por la red, el hilo queda libre.

**Varios sitios, un solo listener.** `HttpListener` multiplexa los puertos por sí
mismo, así que basta añadirle un prefijo por sitio: un único bucle de aceptación
y un único pool de peticiones sirven todos. El puerto de entrada decide qué
carpeta se sirve.

**Contención de rutas.** El handle se abre y se valida en la misma operación con
`GetFinalPathNameByHandle`. Comprobar la ruta y abrirla después dejaría una
ventana en la que un enlace simbólico puede cambiar entre ambos pasos.

**Subida en flujo.** `MultipartReader` analiza `multipart/form-data` sin cargar
nada entero en memoria: un búfer de 64 KB se desliza por la entrada y el
delimitador se busca con Boyer-Moore-Horspool. Cuando no aparece, se entregan
todos los bytes salvo los últimos (longitud del delimitador − 1), que pueden ser
su comienzo partido entre dos lecturas. Un archivo de 2 GB pasa con memoria
constante.

**ZIP en flujo.** `ZipWriter` escribe cada entrada sin volver atrás: el CRC y
el tamaño van en un descriptor detrás del contenido y el directorio central se
escribe al final, así que se envía por HTTP a medida que se lee del disco. El
CRC-32 usa *slicing-by-8* (ocho tablas, ocho bytes por vuelta). Las entradas van
sin comprimir, porque fotos y vídeos ya lo están, y ZIP64 aparece solo cuando
hace falta (archivos de 4 GB o más, desplazamientos por encima de 4 GB o más de
65.535 entradas). Los nombres van en UTF-8 y la fecha también en UTC.

**Configuración a prueba de cortes.** Se escribe en un temporal y se sustituye
con `File.Replace`, que conserva la versión anterior como `.bak`. Si el archivo
principal aparece dañado, se carga la copia antes que los valores de fábrica.

**Código QR.** Implementado desde cero según ISO/IEC 18004: aritmética en
GF(256), Reed-Solomon, las ocho máscaras con su puntuación de penalización y
corrección BCH del bloque de formato. Sin dependencias. Las pruebas lo
contrastan con códigos que leyó un lector real (OpenCV).

**HTTPS sin dependencias.** La autoridad local es RSA 3072 y su clave privada
es una clave CNG **no exportable** del usuario de Windows: ni LanShare puede
sacarla, solo pedir que firme. Lleva **restricciones de nombre** (RFC 5280),
que hacen cumplir los navegadores actuales y Windows: solo puede avalar
`localhost`, nombres `.local` y las redes `10/8`, `172.16/12`, `192.168/16`,
`127/8`, `169.254/16`, `::1`, `fe80::/10` y `fc00::/7`. Aunque alguien robara
la clave, no le serviría para suplantar una web de Internet en los móviles que
la tienen instalada. `100.64/10` (CGNAT, Tailscale) queda fuera a propósito.
.NET Framework 4.8 no sabe escribir esa extensión ni el identificador de la
clave de la autoridad, así que van codificados a mano en DER y validados contra
OpenSSL y el verificador de Chromium. El certificado del servidor (RSA 2048,
solo autenticación de servidor) vive en el almacén del equipo porque quien hace
el TLS es `http.sys` y no ve el del usuario; se asocia a cada puerto con la API
de `http.sys`, lo mismo que `netsh http add sslcert` pero sin lanzar procesos.
La asociación desactiva HTTP/2 para que `HttpListener` se comporte exactamente
igual que por HTTP (respuestas en trozos, WebSocket de la recarga), y no hay
HSTS ni redirección: el puerto HTTP sigue sirviendo, que es por donde el móvil
descarga el certificado.

## Límites conocidos

- La MAC de los móviles modernos es aleatoria por privacidad. Se detecta y se
  marca como tal, pero el OUI no identifica al fabricante en ese caso.
- Chrome congela el User-Agent de Android en `Android 10; K`. Ni la versión ni
  el modelo son reales, y se etiqueta como oculto en lugar de informar un dato
  falso.
- Los Client Hints de alta entropía exigen contexto seguro: por IP de red local
  sobre HTTP no llegan (por la dirección HTTPS, sí).
- En Android, Chrome confía en la autoridad instalada, pero Firefox no la usa
  salvo que se active en sus ajustes, y muchas aplicaciones nativas tampoco.
- HTTPS cubre las direcciones que tenía el equipo al emitir el certificado del
  servidor. Si cambias de red con el servidor en marcha y el equipo estrena IP,
  la nueva dirección no tendrá HTTPS hasta que lo detengas e inicies otra vez.
- La sonda JavaScript no funciona si la página tiene una CSP restrictiva.
- Con la consola remota activa, si además inspeccionas el móvil con las
  herramientas remotas del navegador (`chrome://inspect`, Safari), los
  `console.log` aparecen atribuidos al script de LanShare y no a tu archivo. El
  registro de LanShare sí muestra la línea real.
- El ZIP de una carpeta se genera al vuelo y no anuncia su tamaño, así que el
  navegador no muestra el tiempo restante ni puede reanudar una descarga
  cortada.
- El modo red local no será alcanzable si Windows tiene la red clasificada como
  **pública**: la regla de firewall se limita a los perfiles privado y dominio a
  propósito. Comprueba con `Get-NetConnectionProfile`.

## Fabricantes de MAC

La tabla OUI integrada es corta a propósito (solo prefijos de confianza alta).
Para cobertura completa, descarga
[`oui.txt` del IEEE](https://standards-oui.ieee.org/oui/oui.txt) y déjalo junto
al ejecutable. La etiqueta del panel indica cuántas entradas cargó.

## Licencia

Apache 2.0. Ver [LICENSE](LICENSE).

Autor: [Joel Pasapera](https://github.com/JoelPasapera)
