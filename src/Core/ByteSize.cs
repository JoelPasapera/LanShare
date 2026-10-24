using System.Globalization;

namespace LanShare.Core
{
    /// <summary>Tamanos legibles: 512 B, 3.4 KB, 1.2 MB, 2 GB.</summary>
    public static class ByteSize
    {
        public static string Format(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024L * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024)
                return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}
