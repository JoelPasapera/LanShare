using System;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Nombres de archivo que llegan de la red y control del origen. Es la
    /// frontera de la unica funcion que escribe en disco: aqui cualquier
    /// descuido es un archivo donde no debe o una web ajena dejando ficheros.
    /// </summary>
    public static class UploadTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / UploadNaming");

            runner.Test("un nombre normal no se toca", delegate
            {
                Check.Equal("foto.jpg", UploadNaming.Sanitize("foto.jpg"), "simple");
                Check.Equal("IMG 2026-10-16.HEIC", UploadNaming.Sanitize("IMG 2026-10-16.HEIC"), "con espacios");
                Check.Equal("a\u00f1o \u00e1rbol.pdf", UploadNaming.Sanitize("a\u00f1o \u00e1rbol.pdf"), "acentos");
            });

            runner.Test("SEGURIDAD: las rutas se reducen al ultimo componente", delegate
            {
                Check.Equal("win.ini", UploadNaming.Sanitize("../../Windows/win.ini"), "barras normales");
                Check.Equal("evil.exe", UploadNaming.Sanitize("..\\..\\evil.exe"), "barras invertidas");
                Check.Equal("foto.jpg", UploadNaming.Sanitize("C:\\Users\\otro\\foto.jpg"), "ruta completa de IE");
                Check.Equal(UploadNaming.Fallback, UploadNaming.Sanitize("carpeta/"), "solo carpeta");
            });

            runner.Test("SEGURIDAD: caracteres prohibidos por Windows se sustituyen", delegate
            {
                string clean = UploadNaming.Sanitize("a<b>c:d\"e|f?g*h.txt");
                foreach (char c in "<>:\"|?*")
                    Check.True(clean.IndexOf(c) < 0, "sin '" + c + "' en " + clean);
                Check.Equal("a_b_c_d_e_f_g_h.txt", clean, "sustituidos por guion bajo");
            });

            runner.Test("SEGURIDAD: sin flujos alternativos de NTFS", delegate
            {
                Check.DoesNotContain(UploadNaming.Sanitize("nota.txt:oculto"), ":", "dos puntos");
            });

            runner.Test("caracteres de control se eliminan", delegate
            {
                Check.Equal("ab.txt", UploadNaming.Sanitize("a\u0000b\u001f.txt\u007f"), "controles");
                Check.Equal("linea.txt", UploadNaming.Sanitize("lin\r\nea.txt"), "saltos de linea");
            });

            runner.Test("SEGURIDAD: nombres de dispositivo reservados", delegate
            {
                Check.Equal("_CON", UploadNaming.Sanitize("CON"), "CON");
                Check.Equal("_nul.txt", UploadNaming.Sanitize("nul.txt"), "con extension y minusculas");
                Check.Equal("_COM1.tar.gz", UploadNaming.Sanitize("COM1.tar.gz"), "doble extension");
                Check.Equal("_LPT9", UploadNaming.Sanitize("LPT9"), "LPT9");
                Check.Equal("CONSOLA.txt", UploadNaming.Sanitize("CONSOLA.txt"), "parecido pero no reservado");
                Check.Equal("COM10", UploadNaming.Sanitize("COM10"), "COM10 no es dispositivo");
            });

            runner.Test("puntos y espacios finales se quitan (Windows lo haria en silencio)", delegate
            {
                Check.Equal("informe", UploadNaming.Sanitize("informe..."), "puntos");
                Check.Equal("informe", UploadNaming.Sanitize("informe . . "), "puntos y espacios");
                Check.Equal("informe.pdf", UploadNaming.Sanitize("  informe.pdf  "), "espacios alrededor");
            });

            runner.Test("vacio, punto y dos puntos dan el nombre por defecto", delegate
            {
                Check.Equal(UploadNaming.Fallback, UploadNaming.Sanitize(""), "vacio");
                Check.Equal(UploadNaming.Fallback, UploadNaming.Sanitize(null), "nulo");
                Check.Equal(UploadNaming.Fallback, UploadNaming.Sanitize("."), "punto");
                Check.Equal(UploadNaming.Fallback, UploadNaming.Sanitize(".."), "dos puntos");
                Check.Equal(UploadNaming.Fallback, UploadNaming.Sanitize("   "), "espacios");
            });

            runner.Test("un nombre largo se recorta conservando la extension", delegate
            {
                string clean = UploadNaming.Sanitize(new string('x', 400) + ".jpeg");
                Check.InRange(clean.Length, 1, UploadNaming.MaxNameLength, "longitud acotada");
                Check.True(clean.EndsWith(".jpeg", StringComparison.Ordinal), "extension conservada: " + clean);
            });

            runner.Test("REGRESION: el recorte no parte un emoji por la mitad", delegate
            {
                string name = new string('a', UploadNaming.MaxNameLength - 1) + "\U0001F600" + "b";
                string clean = UploadNaming.Sanitize(name);
                Check.True(clean.Length <= UploadNaming.MaxNameLength, "dentro del maximo");
                Check.False(char.IsHighSurrogate(clean[clean.Length - 1]), "sin medio emoji al final");
                Check.Equal(new string('a', UploadNaming.MaxNameLength - 1), clean, "se corta antes del emoji");

                string entero = UploadNaming.Sanitize(new string('a', UploadNaming.MaxNameLength - 2) + "\U0001F600" + "bcd");
                Check.True(entero.EndsWith("\U0001F600", StringComparison.Ordinal), "si cabe entero, se queda");
            });

            runner.Test("SEGURIDAD: sin caracteres invisibles que disfracen la extension", delegate
            {
                Check.Equal("fotogpj.exe", UploadNaming.Sanitize("foto\u202Egpj.exe"), "anulacion de direccion");
                Check.Equal("ab.txt", UploadNaming.Sanitize("a\u200Bb\u200E.txt"), "espacio de ancho cero y marca LTR");
                Check.Equal("caf\u00e9.txt", UploadNaming.Sanitize("caf\u00e9.txt"), "los acentos se conservan");
            });

            runner.Test("numeracion al estilo del explorador de Windows", delegate
            {
                Check.Equal("foto.jpg", UploadNaming.Candidate("foto.jpg", 1), "primer intento");
                Check.Equal("foto (2).jpg", UploadNaming.Candidate("foto.jpg", 2), "segundo");
                Check.Equal("foto (17).jpg", UploadNaming.Candidate("foto.jpg", 17), "decimoseptimo");
                Check.Equal("LEEME (2)", UploadNaming.Candidate("LEEME", 2), "sin extension");
                Check.Equal(".gitignore (2)", UploadNaming.Candidate(".gitignore", 2), "archivo de punto");
                Check.Equal("datos.tar (2).gz", UploadNaming.Candidate("datos.tar.gz", 2), "doble extension");
            });

            runner.Suite("Core / UploadGuard");

            runner.Test("la propia pagina puede subir", delegate
            {
                Check.True(UploadGuard.IsSameOrigin("http://192.168.1.8:8080", "192.168.1.8:8080", "same-origin"), "LAN");
                Check.True(UploadGuard.IsSameOrigin("http://localhost:3000", "localhost:3000", null), "local sin Sec-Fetch");
                Check.True(UploadGuard.IsSameOrigin("http://LOCALHOST:3000", "localhost:3000", null), "mayusculas");
                Check.True(UploadGuard.IsSameOrigin("http://192.168.1.8", "192.168.1.8", null), "puerto 80 implicito");
            });

            runner.Test("SEGURIDAD: otra web no puede subir con el navegador del usuario", delegate
            {
                Check.False(UploadGuard.IsSameOrigin("http://malicioso.example", "192.168.1.8:8080", null), "otro dominio");
                Check.False(UploadGuard.IsSameOrigin("http://192.168.1.8:3000", "192.168.1.8:8080", null), "otro puerto propio");
                Check.False(UploadGuard.IsSameOrigin("https://192.168.1.8:8080", "192.168.1.8:8080", null), "otro esquema");
                Check.False(UploadGuard.IsSameOrigin("null", "192.168.1.8:8080", null), "origen null de un iframe aislado");
            });

            runner.Test("SEGURIDAD: Sec-Fetch-Site manda cuando el navegador lo envia", delegate
            {
                Check.False(UploadGuard.IsSameOrigin("http://192.168.1.8:8080", "192.168.1.8:8080", "cross-site"), "cross-site");
                Check.False(UploadGuard.IsSameOrigin(null, "192.168.1.8:8080", "same-site"), "same-site sin Origin");
                Check.True(UploadGuard.IsSameOrigin(null, "192.168.1.8:8080", "none"), "navegacion directa");
            });

            runner.Test("clientes que no son navegadores (curl) pueden subir", delegate
            {
                Check.True(UploadGuard.IsSameOrigin(null, "192.168.1.8:8080", null), "sin Origin ni Sec-Fetch");
                Check.True(UploadGuard.IsSameOrigin("", "192.168.1.8:8080", ""), "cabeceras vacias");
            });

            runner.Test("SEGURIDAD: DNS rebinding, un dominio ajeno que apunta a este PC", delegate
            {
                Check.False(UploadGuard.IsSameOrigin("http://evil.example:8080", "evil.example:8080", "same-origin"),
                            "Origin y Host coinciden, pero el nombre es de Internet");
                Check.False(UploadGuard.IsSameOrigin("http://192.168.1.8.nip.io:8080", "192.168.1.8.nip.io:8080", null),
                            "servicios que traducen nombres a IP");
            });

            runner.Test("nombres de host que solo pueden ser este equipo", delegate
            {
                Check.True(UploadGuard.IsLocalHostName("192.168.1.8:8080"), "IPv4 con puerto");
                Check.True(UploadGuard.IsLocalHostName("[::1]:3000"), "IPv6 entre corchetes");
                Check.True(UploadGuard.IsLocalHostName("[fe80::1%12]"), "IPv6 de enlace local con zona");
                Check.True(UploadGuard.IsLocalHostName("localhost"), "localhost");
                Check.True(UploadGuard.IsLocalHostName("app.localhost:5173"), "subdominio de localhost");
                Check.True(UploadGuard.IsLocalHostName("DESKTOP-0Q23CVG:3000"), "nombre del equipo");
                Check.True(UploadGuard.IsLocalHostName("desktop-0q23cvg.local"), "mDNS");
                Check.True(UploadGuard.IsLocalHostName("router.home.arpa"), "home.arpa");
                Check.False(UploadGuard.IsLocalHostName("evil.example"), "dominio de Internet");
                Check.False(UploadGuard.IsLocalHostName("1.2.3.4.example"), "parece una IP pero no lo es");
                Check.False(UploadGuard.IsLocalHostName(""), "vacio");
                Check.False(UploadGuard.IsLocalHostName(null), "nulo");
                Check.False(UploadGuard.IsLocalHostName("[]"), "corchetes vacios");
            });

            runner.Suite("Core / UploadPage y ByteSize");

            runner.Test("la pagina lleva el limite y ningun hueco sin rellenar", delegate
            {
                string html = UploadPage.Render(2L * 1024 * 1024 * 1024);
                Check.Contains(html, "2 GB", "limite legible");
                Check.Contains(html, "2147483648", "limite en bytes para el script");
                // Solo los marcadores propios: los comentarios JSDoc del script
                // usan {{...}} para tipos de objeto y deben quedarse.
                Check.DoesNotContain(html, "{{MAX_", "sin marcadores pendientes");
                Check.Contains(html, "</html>", "documento completo");
            });

            runner.Test("el resultado sin JavaScript escapa el texto", delegate
            {
                string html = UploadPage.RenderResult("<script>alert(1)</script>", false);
                Check.DoesNotContain(html, "<script>alert", "escapado");
                Check.Contains(html, UploadHandler.Endpoint, "enlace para volver");
            });

            runner.Test("tamanos legibles", delegate
            {
                Check.Equal("0 B", ByteSize.Format(0), "cero");
                Check.Equal("512 B", ByteSize.Format(512), "bytes");
                Check.Equal("1.5 KB", ByteSize.Format(1536), "kilobytes");
                Check.Equal("2 MB", ByteSize.Format(2L * 1024 * 1024), "megabytes exactos");
                Check.Equal("2 GB", ByteSize.Format(2L * 1024 * 1024 * 1024), "gigabytes");
                Check.Equal("0 B", ByteSize.Format(-5), "negativo");
            });
        }
    }
}
