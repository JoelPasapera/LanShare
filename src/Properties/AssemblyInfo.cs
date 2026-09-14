using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// Estos campos son los que Windows muestra en las propiedades del ejecutable
// (clic derecho - Propiedades - Detalles). Es autoria declarada, no firma
// criptografica: cualquiera podria escribir aqui otro nombre. Para verificacion
// real hace falta un certificado Authenticode.

[assembly: AssemblyTitle("LanShare")]
[assembly: AssemblyDescription("Servidor de archivos estaticos en LAN con identificacion de dispositivos conectados")]
[assembly: AssemblyCompany("Joel Pasapera")]
[assembly: AssemblyProduct("LanShare")]
[assembly: AssemblyCopyright("Copyright (c) Joel Pasapera 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: NeutralResourcesLanguage("es")]

[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

[assembly: ComVisible(false)]
