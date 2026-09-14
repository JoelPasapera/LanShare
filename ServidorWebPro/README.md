# Servidor Web Pro

Servidor de archivos estáticos para LAN con identificación de dispositivos
conectados. Reescritura en C# sobre .NET Framework 4.8 del script original en
PowerShell.

## Compilar sin instalar nada

```
build.cmd
```

Usa el `csc.exe` que .NET Framework 4.8 ya incluye en todos los Windows 10 y 11.
No hace falta Visual Studio, ni SDK, ni paquetes NuGet. El resultado es
`bin\ServidorWebPro.exe`, un único ejecutable de unos 120 KB que corre en
cualquier Windows con el framework de serie.

Ese compilador es **C# 5**, por eso el código evita interpolación de cadenas,
operador `?.`, `nameof` y demás azúcar posterior. Si modificas algo, mantén esa
restricción o perderás la propiedad de compilar sin instalar nada.

También se incluye `ServidorWebPro.csproj` por si prefieres abrirlo en Visual
Studio; es opcional y produce el mismo binario.

## Uso

1. Elige la carpeta raíz y el puerto.
2. **Solo este equipo**: sirve en `localhost`. No requiere permisos ni abre
   sockets de escucha más allá del propio HTTP, así que Windows no pregunta nada.
3. **Red local**: requiere administrador. Da de alta una regla de firewall para
   el puerto (perfiles privado y dominio) y la retira al detener el servidor.

La pestaña *Clientes conectados* identifica cada dispositivo combinando DNS
inverso, ARP, TTL por ICMP, mDNS, NetBIOS, SSDP, escaneo TCP de puertos con
firma y una sonda JavaScript inyectada en las respuestas HTML.

## Estructura

| Carpeta | Responsabilidad |
|---|---|
| `Core/` | Servidor HTTP, resolución de rutas, contención, MIME, rangos |
| `Identity/` | Sondeo de red, parseo de User-Agent, registro de clientes, veredicto |
| `Net/` | Direcciones LAN, firewall, elevación de privilegios |
| `Ui/` | Formulario, formato del detalle, exportación CSV |

## Diferencias con la versión en PowerShell

- **Concurrencia**: `async`/`await` sobre el thread pool de .NET en lugar de un
  runspace de PowerShell por petición. El coste por petición baja de
  milisegundos a microsegundos y desaparece la contabilidad manual de tareas.
- **Handle validado al abrir**: `PathGuard.TryOpenInsideRoot` abre y comprueba
  la contención en una sola operación, sin tocar `FileStream.SafeFileHandle`
  después.
- **Buffers reutilizados**: `BufferPool` elimina la presión de GC de asignar
  64 KB por petición.
- **Firewall por `netsh`**: no se carga un motor de scripting solo para dar de
  alta una regla.
- **Sin consola**: al compilarse como `winexe` no existe ventana que ocultar.

## Fabricantes de MAC

La tabla OUI integrada es corta a propósito (solo prefijos de confianza alta).
Para cobertura completa descarga `https://standards-oui.ieee.org/oui/oui.txt` y
déjalo junto al ejecutable; se carga al arrancar y la etiqueta del panel indica
cuántas entradas encontró.

## Límites conocidos

- La MAC de móviles modernos es aleatoria por privacidad: se detecta y se marca
  como tal, pero el OUI no identifica al fabricante real en ese caso.
- Chrome congela el User-Agent de Android en `Android 10; K`. Ni la versión ni
  el modelo son reales, y se etiqueta como oculto en lugar de informar un dato
  falso.
- Los Client Hints de alta entropía exigen contexto seguro: por IP de LAN sobre
  HTTP no llegan.
- La sonda JS no funciona si la página tiene una CSP restrictiva.
- El modo LAN no será alcanzable si Windows tiene la red clasificada como
  **pública**: la regla de firewall se limita a los perfiles privado y dominio a
  propósito. Comprueba con `Get-NetConnectionProfile`.
