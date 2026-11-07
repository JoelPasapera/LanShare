using System;
using System.Net;

namespace LanShare.Core
{
    /// <summary>Sistema del movil, para ordenar las instrucciones y elegir como se descarga.</summary>
    public enum CertificatePlatform
    {
        Other,
        Android,
        Apple
    }

    /// <summary>
    /// Pagina de /__certificado: lo que ve el movil para instalar la
    /// autoridad local de LanShare y poder abrir los sitios por HTTPS.
    ///
    /// Se sirve por el HTTP de siempre, porque antes de instalarla el movil
    /// no puede confiar en el HTTPS. Muestra la huella SHA-256 para que se
    /// pueda comparar con la del PC: es la forma de saber que nadie en la
    /// misma Wi-Fi cambio el archivo por el camino.
    ///
    /// Autocontenida y sin JavaScript. La plantilla no lleva comillas dobles
    /// (vive en una cadena literal) y los acentos van como entidades HTML,
    /// para que el codigo fuente siga siendo ASCII.
    /// </summary>
    public static class CertificatePage
    {
        public const string Endpoint = "/__certificado";
        public const string FileName = "LanShare-CA.crt";
        public const string FilePath = Endpoint + "/" + FileName;

        /// <summary>Tipo que reconocen Android e iOS como certificado de autoridad para instalar.</summary>
        public const string CertificateContentType = "application/x-x509-ca-cert";

        public static bool IsPage(string localPath)
        {
            return string.Equals(localPath, Endpoint, StringComparison.Ordinal) ||
                   string.Equals(localPath, Endpoint + "/", StringComparison.Ordinal);
        }

        public static bool IsFile(string localPath)
        {
            return string.Equals(localPath, FilePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Por el User-Agent. El iPad en modo escritorio dice ser un Mac: se
        /// trata igual que un iPhone, porque en un Mac de verdad el
        /// certificado tambien se instala abriendolo.
        /// </summary>
        public static CertificatePlatform Detect(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return CertificatePlatform.Other;
            if (userAgent.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0) return CertificatePlatform.Android;
            if (userAgent.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                userAgent.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                userAgent.IndexOf("iPod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                userAgent.IndexOf("Macintosh", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CertificatePlatform.Apple;
            }
            return CertificatePlatform.Other;
        }

        /// <param name="fingerprint">Huella SHA-256 de la autoridad, "AB:CD:...".</param>
        /// <param name="httpsUrl">Direccion HTTPS del sitio desde este mismo movil.</param>
        public static string Render(string fingerprint, string httpsUrl, CertificatePlatform platform)
        {
            // En Android el archivo tiene que acabar en Descargas (desde la
            // version 11 solo se instala desde Ajustes), y eso lo garantiza el
            // atributo download. En iOS ese atributo lo mandaria a Archivos en
            // vez de abrir la instalacion del perfil.
            string downloadAttribute = platform == CertificatePlatform.Apple ? "" : " download='" + FileName + "'";

            string android = Section(AndroidSteps, "Android", platform == CertificatePlatform.Android);
            string apple = Section(AppleSteps, "iPhone y iPad", platform == CertificatePlatform.Apple);

            return Template
                .Replace("{{DOWNLOAD}}", downloadAttribute)
                .Replace("{{FILE}}", FilePath)
                .Replace("{{FIRST}}", platform == CertificatePlatform.Apple ? apple : android)
                .Replace("{{SECOND}}", platform == CertificatePlatform.Apple ? android : apple)
                .Replace("{{FINGERPRINT}}", BreakablePairs(WebUtility.HtmlEncode(fingerprint ?? "")))
                .Replace("{{HTTPS_URL}}", WebUtility.HtmlEncode(httpsUrl ?? ""));
        }

        /// <summary>
        /// Deja cortar la huella solo tras los dos puntos: partida a mitad de
        /// un par ("E1:0" y "6:2B") no hay quien la compare con la del PC. Se
        /// aplica sobre el texto ya escapado, que no puede traer etiquetas.
        /// </summary>
        private static string BreakablePairs(string encodedFingerprint)
        {
            return encodedFingerprint.Replace(":", ":<wbr>");
        }

        private static string Section(string steps, string title, bool open)
        {
            return "<details" + (open ? " open" : "") + "><summary>" + title + "</summary><ol>" + steps +
                   "</ol></details>";
        }

        private const string AndroidSteps =
            "<li>Toca <b>Descargar certificado</b>. Se guarda en Descargas.</li>" +
            "<li>Abre <b>Ajustes</b> y busca <b>certificado</b> (suele estar en Seguridad, " +
            "<i>Cifrado y credenciales</i>).</li>" +
            "<li>Toca <b>Instalar un certificado</b> y luego <b>Certificado de CA</b>. Android avisa de que " +
            "es para redes de confianza: elige <b>Instalar de todos modos</b>.</li>" +
            "<li>Elige <b>" + FileName + "</b> en Descargas. Puede pedirte el PIN del m&oacute;vil.</li>";

        private const string AppleSteps =
            "<li>Toca <b>Descargar certificado</b> y luego <b>Permitir</b>: se descarga un perfil.</li>" +
            "<li>En <b>Ajustes</b>, toca <b>Perfil descargado</b> (o General, <i>VPN y gesti&oacute;n de " +
            "dispositivos</i>) y luego <b>Instalar</b>.</li>" +
            "<li>Ve a <b>Ajustes, General, Informaci&oacute;n, Ajustes de confianza de certificados</b> y " +
            "activa <b>LanShare CA</b>. Sin este paso Safari no conf&iacute;a en &eacute;l.</li>";

        private const string Template =
@"<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Certificado de LanShare</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--text:#1d232b;--muted:#5b6573;--line:#dfe3e8;--accent:#1f6feb}
@media (prefers-color-scheme:dark){:root{--bg:#0f1318;--card:#171c22;--text:#e6e9ee;--muted:#9aa4b1;--line:#2a313a;--accent:#4c8dff}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:16px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
main{max-width:560px;margin:0 auto;padding:20px 16px 40px}
h1{font-size:22px;margin:4px 0 8px}
h2{font-size:17px;margin:22px 0 6px}
p{margin:8px 0}
.muted{color:var(--muted);font-size:14px}
.button{display:block;margin:18px 0;padding:14px;border-radius:12px;background:var(--accent);color:#fff;text-align:center;font-weight:600;text-decoration:none}
details{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:10px 14px;margin:10px 0}
summary{font-weight:600;cursor:pointer}
ol{padding-left:20px;margin:10px 0 4px}
li{margin:6px 0}
.fingerprint{font:13px/1.6 ui-monospace,Consolas,monospace;overflow-wrap:anywhere;background:var(--card);border:1px solid var(--line);border-radius:10px;padding:10px}
a{color:var(--accent)}
</style>
</head>
<body>
<main>
<h1>Certificado de LanShare</h1>
<p>Inst&aacute;lalo una vez en este m&oacute;vil para abrir por HTTPS, sin avisos, las p&aacute;ginas que sirve
el PC. As&iacute; funciona lo que los navegadores reservan a las conexiones seguras: WebGPU, service workers,
la c&aacute;mara o el portapapeles.</p>
<p class='muted'>Solo vale para direcciones de tu red local: el propio certificado proh&iacute;be usarlo con
webs de Internet.</p>
<a class='button' href='{{FILE}}'{{DOWNLOAD}}>Descargar certificado</a>
{{FIRST}}
{{SECOND}}
<h2>Comprueba la huella</h2>
<p class='muted'>Tiene que coincidir con la que muestra LanShare en el PC (Configuraci&oacute;n, pesta&ntilde;a HTTPS).
Si no coincide, no lo instales.</p>
<p class='fingerprint'>{{FINGERPRINT}}</p>
<h2>Despu&eacute;s</h2>
<p><a href='{{HTTPS_URL}}'>Abrir la versi&oacute;n segura de este sitio</a></p>
</main>
</body>
</html>";
    }
}
