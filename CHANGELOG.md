# Cambios

## 2.1

### Nuevo

- **HTTPS en la red local.** Cada sitio puede servirse además por un segundo
  puerto con HTTPS (*Editar sitio*), para probar en el móvil lo que los
  navegadores solo permiten con conexión segura: WebGPU, *service workers*,
  cámara. LanShare crea una autoridad de certificación propia
  del equipo, que se instala una vez en cada móvil desde un QR (`/__certificado`,
  con los pasos de Android y de iPhone y la huella SHA-256 para comprobarla), y
  con ella firma el certificado del servidor, que se renueva solo. La autoridad
  lleva restricciones de nombre que la limitan a direcciones privadas,
  `localhost` y nombres `.local`, y su clave es no exportable: aun robada, no
  serviría para suplantar webs de Internet. Las restricciones de nombre y el
  identificador de clave se codifican a mano en DER (.NET Framework 4.8 no los
  trae) y la asociación con el puerto se hace con la API de `http.sys`, sin
  `netsh`. Desde *Configuración → HTTPS* se ve la huella, se puede confiar en
  la autoridad en el propio PC y borrarlo todo. Apagado de serie.
- **Aislamiento entre orígenes** (Configuración → Desarrollo): envía
  `Cross-Origin-Opener-Policy` y `Cross-Origin-Embedder-Policy` para que las
  páginas puedan usar `SharedArrayBuffer` y WebAssembly con hilos, en
  `localhost` o por HTTPS. Apagado de serie, porque bloquea los recursos de
  otros dominios que no lo autoricen.
- **Recarga automática al guardar.** Vigila la carpeta de cada sitio y recarga
  las páginas abiertas en todos los dispositivos a la vez. Si solo cambió una
  hoja de estilos, la sustituye en caliente sin recargar. Va por WebSocket para
  no consumir el límite de seis conexiones por origen del navegador. Apagada de
  serie: Configuración → Desarrollo.
- **Consola remota.** Los errores de JavaScript, las promesas rechazadas, los
  recursos que no cargan y los `console.log` de las páginas abiertas en el
  móvil aparecen en el registro del PC con archivo y línea. Agrupa repetidos,
  limita el ritmo por dispositivo y no se inyecta en el propio PC. Apagada de
  serie: Configuración → Desarrollo.
- **Subida de archivos desde el móvil** en `/__subir`, con progreso por archivo.
  Analizador `multipart/form-data` propio, en flujo, con Boyer-Moore-Horspool:
  memoria constante sea cual sea el tamaño. Solo desde la propia página, sin
  sobrescribir nunca, con nombres saneados para Windows y carpeta de destino
  fuera de los sitios servidos. Apagada de serie: Configuración → Subida.
- **Listado de carpetas renovado:** ruta navegable tramo a tramo, orden por
  nombre, tamaño o fecha, filtro por nombre y **descarga de la carpeta en ZIP**,
  generada en flujo con un escritor ZIP propio (CRC-32 *slicing-by-8*,
  descriptores de datos, ZIP64 cuando hace falta).
- **Editar sitios con el servidor en marcha:** añadir, editar, quitar o marcar
  un sitio se aplica reiniciando el servidor solo, sin abrir pestañas y
  conservando los dispositivos identificados, las reglas de firewall y el
  filtro del registro. Los cambios que no tocan lo que se sirve no reinician.
  Antes había que detenerlo.
- **Añadir un sitio arrastrando su carpeta** (o una de sus páginas) a la
  ventana, también con LanShare elevado.
- **Atajos de teclado:** F5, Ctrl+N, Ctrl+, , Ctrl+S, y sobre la tabla Intro,
  F2, Supr y Ctrl+C.
- **Cajas de altura ajustable.** Los bordes de la tabla de sitios, el estado,
  el registro, la tabla de clientes y su detalle se arrastran con el cursor de
  redimensionar de Windows: la caja crece y las de al lado se apartan, cada una
  hasta su mínimo. La franja bajo el registro estira la ventana, doble clic en
  un borde devuelve las alturas originales y el reparto se guarda al soltar. Si
  la ventana se achica, las cajas encogen y al volver a haber sitio recuperan
  su altura. El divisor de clientes ya no puede fallar con la ventana pequeña,
  así que la ventana admite un alto mínimo menor.
- **Bandeja del sistema**: icono con menú (mostrar, iniciar o detener, salir),
  aviso cuando el servidor arranca oculto o cae, y opción de minimizar a la
  bandeja.
- **Iniciar con Windows**: tarea programada elevada cuando hay administrador,
  clave `Run` cuando no. La tarea no se detiene con batería ni a los tres días.
- **Línea de comandos**: `--start` y `--tray`.
- **Una sola instancia**: abrir LanShare otra vez trae al frente la ventana que
  ya corre, aunque esté oculta o elevada.
- **Menú contextual de sitios**: abrir en el navegador, abrir la carpeta,
  copiar URL local o de red, QR del sitio y QR de la página de subida.
- **Estadísticas de la caché gzip** en la barra del registro: archivos,
  memoria y porcentaje de aciertos.
- **Configuración en pestañas**, con las opciones de identificación de
  clientes (sonda, escaneo de puertos, nombres por mDNS/NetBIOS) por fin
  desactivables.

### Mejorado

- **El QR propone primero la dirección a la que llega el móvil.** Antes usaba
  la primera que enumeraba Windows, que en un equipo de desarrollo puede ser la
  de WSL, Hyper-V, VirtualBox o VMware. Ahora va primero el adaptador con
  puerta de enlace (el que conecta con el router), después los físicos y al
  final los virtuales y las VPN. El desplegable muestra el nombre de cada
  adaptador, y uno que falle al consultarlo ya no oculta a los demás.
- El **registro** ocupa una línea por entrada en lugar de dos.
- El **panel de estado** muestra cinco sitios sin desplazarse.
- El **error de arranque** dice qué sitio falla y qué proceso tiene el puerto,
  en lugar de "otro programa ocupa alguno de ellos".
- Los arranques automáticos ya **no abren una pestaña por proyecto**.
- Al **escalar privilegios** con el servidor en marcha, la nueva instancia lo
  vuelve a levantar.
- **Un sitio nuevo propone un puerto libre** en vez de nacer siempre con el
  8080, aunque ya lo usara otro proyecto.
- Los arranques desatendidos (al iniciar sesión, desde la bandeja) **ya no
  muestran diálogos**: lo que no puede arrancar se omite con su motivo en el
  registro y un aviso en la bandeja.
- `build.cmd` y `build-tests.cmd` van con finales de línea CRLF (fijado en
  `.gitattributes`): con solo LF, `cmd.exe` puede no encontrar sus etiquetas.

### Corregido

- **Ningún móvil leía los códigos QR.** Los quince bits de formato (nivel de
  corrección y máscara) se escribían traspuestos, fila por columna, así que el
  lector no sabía qué máscara quitar. Las pruebas no lo veían porque su
  decodificador cometía el mismo error. Ahora siguen la norma, el decodificador
  de las pruebas lee como ZXing y ambos se contrastan con códigos que leyó
  OpenCV.
- La **configuración** se escribía encima del archivo: un apagado a mitad la
  corrompía y la sesión siguiente arrancaba sin sitios. Ahora la escritura es
  atómica y se conserva la versión anterior como respaldo.
- El README afirmaba que el escaneo de puertos y la sonda se podían desactivar
  desde Configuración, y no era cierto. Ahora sí.
- Al cerrar con la ventana minimizada se guardaba un tamaño de ventana diminuto.
- **Detener el servidor dos veces a la vez** (desde la bandeja y desde la
  ventana) acababa en un error no controlado y podía dejar la ventana sin cerrar.
- Al **cerrar sesión** con el servidor en marcha, Windows mostraba "LanShare
  impide cerrar la sesión". Ahora se detiene en el acto.
- **Iniciar con Windows sin administrador** con algún sitio en red local
  mostraba un diálogo al entrar y no arrancaba ninguno. Ahora arranca los
  locales y avisa en la bandeja; lo mismo con una carpeta que aún no existe
  (una unidad externa sin montar), que antes bloqueaba a todos.
- Un arranque abortado dejaba puesta la marca de arranque silencioso y el
  siguiente arranque manual no abría las pestañas.
- **Recarga automática:** crear un archivo dentro de una carpeta provocaba una
  recarga completa en vez del cambio de CSS en caliente (llegaba también el
  cambio de la propia carpeta). Se ignoran los temporales de JetBrains.
- Las pestañas abiertas en una sesión anterior reintentaban conectar para
  siempre cuando la recarga estaba apagada, llenando el registro de avisos.
- **Subida:** protección frente a *DNS rebinding*, sin caracteres invisibles
  que disfracen la extensión, sin emojis partidos por la mitad al recortar un
  nombre largo, limpieza de temporales de subidas interrumpidas, y una ruta de
  destino mal escrita ya no tumba el diálogo de Configuración.
- La sonda leía sin límite un cuerpo que llegase troceado.
- Cancelar el aviso de UAC ya no muestra un error.
- Volver de la bandeja respeta la ventana maximizada.
- Las casillas de los sitios aparecían desmarcadas al abrir la ventana tras
  arrancar oculto en la bandeja.
- Hacer **doble clic en un sitio para editarlo** cambiaba también su casilla de
  "servir al iniciar".
- Abrir Configuración ya no lanza `schtasks.exe` para saber si hay inicio con
  Windows (se mira la caché de tareas del registro) y una llamada colgada ya no
  congela la ventana.
- Al escalar privilegios, si la ventana anterior no se cierra en un minuto, se
  avisa en lugar de no abrir nada.
- La configuración reintenta el guardado si un antivirus bloquea el archivo un
  instante.
- Las **reglas de firewall** ya no se duplican cuando una sesión anterior no
  llegó a borrarlas (apagado, cierre forzado), y las llamadas a `netsh` tienen
  un tope de tiempo real.
- Un **arranque desatendido** con un puerto ocupado por otro programa ya no deja
  sin servir a los demás sitios: se aparta ese sitio y se arranca el resto.

### Pruebas

De 244 a 514 pruebas. Lo nuevo con más peso: el analizador multipart con
lecturas troceadas de 1 a 70.000 bytes y casi-delimitadores dentro del
contenido, Horspool contrastado con la búsqueda ingenua, saneado de nombres
(rutas, dispositivos reservados, puntos finales, caracteres invisibles, emojis
en el corte), control de origen y de nombre de host de la subida, guardado
atómico de la configuración con recuperación desde la copia, la definición XML
de la tarea programada y el vigilante de archivos sobre disco real. Y de lo
añadido después: el CRC-32 contrastado con un cálculo bit a bit, los ZIP leídos
con `ZipArchive` de .NET (también en ZIP64), la consola remota (lectura del
lote hostil, línea del registro, ritmo por cliente, inserción al principio de
`<head>`), las migas y los datos de orden del listado, qué sitio se propone
al arrastrar una carpeta o una página, los códigos QR contrastados con
matrices que leyó OpenCV, el orden en que se proponen las direcciones de red,
el reparto de altura entre las cajas de la ventana y HTTPS: el DER de las
restricciones de nombre y del identificador de clave contrastado byte a byte
con la biblioteca `cryptography` de Python, el lector DER ante cada corte y
cada byte cambiado, la emisión de los dos certificados con claves efímeras y su
cadena validada por el verificador de Windows (que rechaza una IP o un dominio
de Internet), la clave del servidor de ida y vuelta por PFX, el tamaño de las
estructuras de `http.sys`, qué certificados y asociaciones se limpian sin
tocar los de otro usuario del equipo, qué direcciones entran en el
certificado, la página del certificado y el control de origen por HTTPS.

## 2.0

Reescritura en C# sobre .NET Framework 4.8: varios sitios a la vez, archivo de
inicio por sitio, detección de puertos libres con el proceso que los ocupa,
registro filtrable y guardable, caché de compresión e identificación de
clientes. Ver el historial del repositorio para el detalle.
