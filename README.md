# LanShare

Comparte una carpeta con tu teléfono, tu tablet o cualquier equipo de la red
local. Un solo ejecutable de unos 160 KB que corre en cualquier Windows 10 u 11
sin instalar nada. PUEDES ABRIR PAGINAS WEB DESDE TU CELULAR USANDO TU PC COMO SERVIDOR
o abrirla desde tu misma pc (localhost)

<!-- Sustituye por una captura real: docs/capturas/principal.png -->
<!-- ![Ventana principal](docs/capturas/principal.png) -->

## Qué hace

- Sirve **varias carpetas a la vez**, cada una en su propio puerto: un sitio por
  proyecto, todos desde una sola ventana y un solo registro.
- Cada sitio elige su alcance por separado: `localhost` o toda la red local.
- Genera un **código QR** de la dirección para abrirla en el móvil sin teclear la IP.
- **Lista las carpetas** que no tienen archivo de inicio, con tamaños y fechas.
- Cada sitio elige su **archivo de inicio** de entre los `.html` que hay en la
  carpeta, o se sirve como carpeta y lista siempre su contenido.
- **Comprime** HTML, CSS, JS, JSON y SVG con gzip (entre un 60 % y un 80 % menos).
- Soporta **descargas parciales** (`Range`), así que el vídeo admite búsqueda.
- **Identifica cada cliente** combinando DNS inverso, ARP, TTL por ICMP, mDNS,
  NetBIOS, SSDP, escaneo TCP de puertos con firma y una sonda JavaScript que
  reporta la GPU y la resolución real.

> **Aviso.** Para identificar a los clientes, el programa hace **sondeo activo**:
> abre conexiones TCP hacia 21 puertos del dispositivo que se conecta e inyecta
> un `<script>` en las páginas HTML que sirve. En tu propia red es tu decisión,
> pero conviene saberlo: en una red corporativa un escaneo de puertos suele
> disparar las alertas del sistema de detección de intrusos. Ambas cosas se
> pueden desactivar desde *Configuración*.

## Instalación

Descarga `LanShare.exe` de la sección
[Releases](https://github.com/JoelPasapera/LanShare/releases) y ejecútalo. No
requiere instalador ni dependencias: .NET Framework 4.8 ya viene con Windows.

Para el modo red local hace falta administrador (clic derecho → *Ejecutar como
administrador*, o el botón dentro de la aplicación).

## Compilar desde el código
Solo entra a la carpeta 'src' luego dale doble click a 'build.cmd' (es el encargado de compilar todo el proyecto en un solo archivo .exe) luego sal de esa carpeta y ve a la carpeta 'bin' (ahi se guardan los binarios), ahi estará el ejecutable '.exe' el cual es el programa en si mismo ya listo para usar

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
  de servicio y verifica los síndromes Reed-Solomon con matemática distinta a la
  del codificador. Si dan cero y el texto vuelve intacto, un lector real podrá
  interpretarlo. Encontró un fallo auténtico: los quince bits de formato se
  repartían 8 y 7 cuando la norma pide 7 y 8.
- Las pruebas marcadas **`REGRESION:`** cubren fallos que ya ocurrieron una vez.
- Las marcadas **`SEGURIDAD:`** cubren la contención de rutas, el escapado del
  listado de carpetas y los punteros circulares de DNS.

## Estructura

| Carpeta | Responsabilidad |
|---|---|
| `src/Core/` | Servidor HTTP asíncrono multi-sitio, resolución de rutas, contención, MIME, rangos, compresión, caché, QR |
| `src/Identity/` | Sondeo de red, parseo de User-Agent, registro de clientes, fusión de señales |
| `src/Net/` | Direcciones LAN, firewall, elevación de privilegios |
| `src/Ui/` | Formulario, diálogos, exportación CSV |
| `tests/` | Batería de pruebas, sin dependencias externas |
| `legacy/` | Versión 1 en PowerShell, congelada |

Las piezas de `Core` e `Identity` son funciones puras sin estado
(`RangeParser`, `UaParser`, `StaticFileResolver`, `DnsWire`, `VerdictEngine`,
`EntityTag`, `QrCode`), lo que permite probarlas sin levantar una ventana.

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

**Código QR.** Implementado desde cero según ISO/IEC 18004: aritmética en
GF(256), Reed-Solomon, las ocho máscaras con su puntuación de penalización y
corrección BCH del bloque de formato. Sin dependencias.

## Límites conocidos

- La MAC de los móviles modernos es aleatoria por privacidad. Se detecta y se
  marca como tal, pero el OUI no identifica al fabricante en ese caso.
- Chrome congela el User-Agent de Android en `Android 10; K`. Ni la versión ni
  el modelo son reales, y se etiqueta como oculto en lugar de informar un dato
  falso.
- Los Client Hints de alta entropía exigen contexto seguro: por IP de red local
  sobre HTTP no llegan.
- La sonda JavaScript no funciona si la página tiene una CSP restrictiva.
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
