# Versión 1 — PowerShell (congelada)

**Este código no se mantiene.** El proyecto vivo está en [`../src/`](../src/).

Se conserva por dos motivos:

1. Contiene implementaciones que costaron trabajo y que sirven de contraste si
   alguna pieza de la versión en C# se comporta de forma extraña: la
   comprobación anti-symlink por handle, el parseo del paquete NBSTAT con su
   codificación de nibbles, la cosecha de registros A de mDNS.

2. Documenta **por qué** la versión actual está hecha como está. El script usa
   un runspace de PowerShell por petición; la versión en C# usa `async`/`await`
   sobre el thread pool. Ver el antes y el después explica la decisión mejor que
   cualquier comentario.

## Qué le falta respecto a la versión actual

Listado de carpetas, compresión gzip, caché condicional con ETag, código QR,
alias de dispositivos, configuración persistente y el panel de configuración.

## Si aun así quieres ejecutarlo

```
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File ServidorWebPro.ps1
```

O haz doble clic en `Iniciar.vbs`, que lo lanza sin que parpadee la consola.
