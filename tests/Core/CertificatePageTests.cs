using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// La pagina /__certificado: rutas, deteccion del movil y lo que se le
    /// muestra. Se sirve por HTTP a un movil que aun no confia en nada, asi
    /// que lo que viene de fuera (la huella, la direccion) va siempre escapado.
    /// </summary>
    public static class CertificatePageTests
    {
        private const string AndroidAgent =
            "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36";

        private const string IphoneAgent =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

        /// <summary>Un iPad en modo escritorio se presenta como un Mac.</summary>
        private const string IpadDesktopAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";

        private const string WindowsAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

        private const string Fingerprint = "AB:CD:EF";

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / pagina del certificado");

            runner.Test("rutas de la pagina y del archivo", delegate
            {
                Check.True(CertificatePage.IsPage("/__certificado"), "pagina");
                Check.True(CertificatePage.IsPage("/__certificado/"), "pagina con barra");
                Check.False(CertificatePage.IsPage("/__certificado/otra"), "subruta");
                Check.False(CertificatePage.IsPage("/certificado"), "sin prefijo");

                Check.True(CertificatePage.IsFile("/__certificado/LanShare-CA.crt"), "archivo");
                Check.True(CertificatePage.IsFile("/__certificado/lanshare-ca.CRT"), "archivo sin distinguir mayusculas");
                Check.False(CertificatePage.IsFile("/__certificado/otro.crt"), "otro archivo");
                Check.False(CertificatePage.IsFile("/__certificado"), "la pagina no es el archivo");
            });

            runner.Test("reconoce Android, iPhone, iPad y el resto", delegate
            {
                Check.Equal(CertificatePlatform.Android, CertificatePage.Detect(AndroidAgent), "Android");
                Check.Equal(CertificatePlatform.Apple, CertificatePage.Detect(IphoneAgent), "iPhone");
                Check.Equal(CertificatePlatform.Apple, CertificatePage.Detect(IpadDesktopAgent), "iPad en modo escritorio");
                Check.Equal(CertificatePlatform.Other, CertificatePage.Detect(WindowsAgent), "Windows");
                Check.Equal(CertificatePlatform.Other, CertificatePage.Detect(""), "vacio");
                Check.Equal(CertificatePlatform.Other, CertificatePage.Detect(null), "nulo");
            });

            runner.Test("en Android se descarga y sus pasos van primero y abiertos", delegate
            {
                string html = CertificatePage.Render(Fingerprint, "https://192.168.1.5:8443/", CertificatePlatform.Android);
                Check.Contains(html, "href='/__certificado/LanShare-CA.crt' download='LanShare-CA.crt'", "descarga");
                Check.Contains(html, "<details open><summary>Android</summary>", "Android abierto");
                Check.Contains(html, "<details><summary>iPhone y iPad</summary>", "iPhone cerrado");
                Check.True(html.IndexOf("Android</summary>", System.StringComparison.Ordinal) <
                           html.IndexOf("iPhone y iPad</summary>", System.StringComparison.Ordinal), "orden");
            });

            runner.Test("en iPhone se abre el perfil en vez de descargarlo", delegate
            {
                string html = CertificatePage.Render(Fingerprint, "https://192.168.1.5:8443/", CertificatePlatform.Apple);
                Check.Contains(html, "href='/__certificado/LanShare-CA.crt'>", "enlace sin descarga");
                Check.DoesNotContain(html, " download=", "sin atributo download");
                Check.Contains(html, "<details open><summary>iPhone y iPad</summary>", "iPhone abierto");
                Check.True(html.IndexOf("iPhone y iPad</summary>", System.StringComparison.Ordinal) <
                           html.IndexOf("Android</summary>", System.StringComparison.Ordinal), "orden");
            });

            runner.Test("muestra la huella y el enlace a la version segura", delegate
            {
                string html = CertificatePage.Render(Fingerprint, "https://192.168.1.5:8443/", CertificatePlatform.Other);
                Check.Contains(html, "<p class='fingerprint'>AB:<wbr>CD:<wbr>EF</p>", "huella, cortable entre pares");
                Check.Contains(html, "href='https://192.168.1.5:8443/'", "version segura");
                Check.DoesNotContain(html, "{{", "sin marcadores sin sustituir");
            });

            runner.Test("CLAVE: lo que viene de fuera va escapado", delegate
            {
                // La direccion sale de la cabecera Host, que escribe el cliente.
                // La plantilla no lleva <script>: si aparece, se colo sin escapar.
                string html = CertificatePage.Render("<script>x</script>", "https://x'onmouseover='alert(1)&y/",
                                                     CertificatePlatform.Other);
                Check.DoesNotContain(html, "<script>", "huella escapada");
                Check.Contains(html, "<p class='fingerprint'>&lt;script&gt;x&lt;/script&gt;</p>", "huella como texto");
                Check.DoesNotContain(html, "x'onmouseover", "la comilla no cierra el atributo");
                Check.Contains(html, "&amp;y/", "ampersand escapado");
            });

            runner.Test("sin huella ni direccion no falla", delegate
            {
                string html = CertificatePage.Render(null, null, CertificatePlatform.Other);
                Check.Contains(html, "<p class='fingerprint'></p>", "huella vacia");
            });
        }
    }
}
