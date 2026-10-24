using System;
using System.Text;

namespace LanShare.Core
{
    /// <summary>
    /// Cabecera Content-Disposition para descargas, segun RFC 6266: un nombre
    /// ASCII de repuesto en filename y el nombre real en filename*, codificado
    /// en UTF-8 con porcentajes (RFC 5987). Los navegadores actuales usan el
    /// segundo; el primero queda para los que no lo entienden.
    ///
    /// La cabecera sale siempre en ASCII puro: HttpListener no codifica bien
    /// otra cosa y un salto de linea en un nombre seria una inyeccion de
    /// cabeceras.
    /// </summary>
    public static class ContentDisposition
    {
        public static string Attachment(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) fileName = "descarga";

            StringBuilder ascii = new StringBuilder(fileName.Length);
            foreach (char c in fileName)
            {
                bool plain = c >= 0x20 && c <= 0x7E && c != '"' && c != '\\' && c != '%' && c != ';';
                ascii.Append(plain ? c : '_');
            }

            StringBuilder encoded = new StringBuilder(fileName.Length * 3);
            foreach (byte b in Encoding.UTF8.GetBytes(fileName))
            {
                if (IsAttrChar(b)) encoded.Append((char)b);
                else encoded.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 0xF]);
            }

            return "attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + encoded;
        }

        /// <summary>attr-char de RFC 5987: lo que puede ir sin codificar.</summary>
        private static bool IsAttrChar(byte b)
        {
            if (b >= 'a' && b <= 'z') return true;
            if (b >= 'A' && b <= 'Z') return true;
            if (b >= '0' && b <= '9') return true;
            return "!#$&+-.^_`|~".IndexOf((char)b) >= 0;
        }
    }
}
