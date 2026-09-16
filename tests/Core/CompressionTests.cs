using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    public static class CompressionTests
    {
        private static byte[] Texto(int veces)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < veces; i++) sb.Append("body { margin: 0; padding: 0; } ");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static byte[] Descomprimir(byte[] comprimido)
        {
            using (MemoryStream entrada = new MemoryStream(comprimido))
            using (GZipStream gzip = new GZipStream(entrada, CompressionMode.Decompress))
            using (MemoryStream salida = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int leido;
                while ((leido = gzip.Read(buffer, 0, buffer.Length)) > 0) salida.Write(buffer, 0, leido);
                return salida.ToArray();
            }
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / CompressionPolicy");

            runner.Test("se detecta si el cliente admite gzip", delegate
            {
                Check.True(CompressionPolicy.ClientAcceptsGzip("gzip"), "solo gzip");
                Check.True(CompressionPolicy.ClientAcceptsGzip("gzip, deflate, br"), "en lista");
                Check.True(CompressionPolicy.ClientAcceptsGzip("GZIP"), "mayusculas");
                Check.False(CompressionPolicy.ClientAcceptsGzip("deflate, br"), "sin gzip");
                Check.False(CompressionPolicy.ClientAcceptsGzip(null), "nulo");
                Check.False(CompressionPolicy.ClientAcceptsGzip(""), "vacio");
            });

            runner.Test("solo se comprimen tipos de texto", delegate
            {
                Check.True(CompressionPolicy.IsCompressibleType("text/html; charset=utf-8"), "html");
                Check.True(CompressionPolicy.IsCompressibleType("text/css"), "css");
                Check.True(CompressionPolicy.IsCompressibleType("application/json"), "json");
                Check.True(CompressionPolicy.IsCompressibleType("image/svg+xml"), "svg");
                Check.True(CompressionPolicy.IsCompressibleType("application/wasm"), "wasm");
            });

            runner.Test("lo que ya viene comprimido se deja en paz", delegate
            {
                Check.False(CompressionPolicy.IsCompressibleType("image/png"), "png");
                Check.False(CompressionPolicy.IsCompressibleType("image/jpeg"), "jpeg");
                Check.False(CompressionPolicy.IsCompressibleType("video/mp4"), "mp4");
                Check.False(CompressionPolicy.IsCompressibleType("font/woff2"), "woff2");
                Check.False(CompressionPolicy.IsCompressibleType("application/zip"), "zip");
                Check.False(CompressionPolicy.IsCompressibleType(null), "nulo");
            });

            runner.Test("hay un suelo y un techo de tamano", delegate
            {
                Check.False(CompressionPolicy.IsCompressible("text/css", 100), "demasiado pequeno");
                Check.False(CompressionPolicy.IsCompressible("text/css", 1023), "justo por debajo");
                Check.True(CompressionPolicy.IsCompressible("text/css", 1024), "en el limite inferior");
                Check.True(CompressionPolicy.IsCompressible("text/css", 4096), "normal");
                Check.False(CompressionPolicy.IsCompressible("text/css",
                            CompressionPolicy.MaximumSize + 1), "por encima del techo");
            });

            runner.Test("CRITICO: comprimir y descomprimir devuelve lo mismo", delegate
            {
                byte[] original = Texto(200);
                byte[] comprimido = CompressionPolicy.Gzip(original);

                Check.NotNull(comprimido, "hubo compresion");
                Check.True(comprimido.Length < original.Length, "es mas pequeno");
                Check.BytesEqual(original, Descomprimir(comprimido), "ida y vuelta");
            });

            runner.Test("contenido incompresible devuelve nulo en vez de crecer", delegate
            {
                Random random = new Random(1234);
                byte[] ruido = new byte[8192];
                random.NextBytes(ruido);

                byte[] comprimido = CompressionPolicy.Gzip(ruido);
                if (comprimido != null) Check.True(comprimido.Length < ruido.Length, "nunca mayor");
            });

            runner.Test("un texto repetitivo se reduce mucho", delegate
            {
                byte[] original = Texto(500);
                byte[] comprimido = CompressionPolicy.Gzip(original);
                Check.NotNull(comprimido, "hubo compresion");
                Check.True(comprimido.Length < original.Length / 4, "al menos un 75% menos");
            });

            runner.Suite("Core / CompressionCache");

            runner.Test("la clave depende de ruta, tamano, fecha y variante", delegate
            {
                DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                string baseKey = CompressionCache.BuildKey(@"C:\a.js", 100, t, "");

                Check.NotEqual(baseKey, CompressionCache.BuildKey(@"C:\b.js", 100, t, ""), "otra ruta");
                Check.NotEqual(baseKey, CompressionCache.BuildKey(@"C:\a.js", 101, t, ""), "otro tamano");
                Check.NotEqual(baseKey, CompressionCache.BuildKey(@"C:\a.js", 100, t.AddSeconds(1), ""), "otra fecha");
                Check.NotEqual(baseKey, CompressionCache.BuildKey(@"C:\a.js", 100, t, "i"), "otra variante");
                Check.Equal(baseKey, CompressionCache.BuildKey(@"C:\a.js", 100, t, ""), "determinista");
            });

            runner.Test("guardar y recuperar", delegate
            {
                CompressionCache.Clear();
                byte[] datos = new byte[] { 1, 2, 3, 4, 5 };

                CompressionCache.Store("clave-a", datos);
                Check.BytesEqual(datos, CompressionCache.Get("clave-a"), "recuperado");
                Check.Equal(1, CompressionCache.Count, "una entrada");
                Check.Equal(5L, CompressionCache.Bytes, "bytes contabilizados");
            });

            runner.Test("clave desconocida o nula devuelve nulo", delegate
            {
                CompressionCache.Clear();
                Check.Null(CompressionCache.Get("no-existe"), "desconocida");
                Check.Null(CompressionCache.Get(null), "nula");
                Check.Null(CompressionCache.Get(""), "vacia");
            });

            runner.Test("guardar con clave o contenido nulo no hace nada", delegate
            {
                CompressionCache.Clear();
                CompressionCache.Store(null, new byte[] { 1 });
                CompressionCache.Store("x", null);
                Check.Equal(0, CompressionCache.Count, "sigue vacia");
            });

            runner.Test("Clear vacia entradas y contador", delegate
            {
                CompressionCache.Clear();
                CompressionCache.Store("a", new byte[100]);
                CompressionCache.Store("b", new byte[200]);
                Check.Equal(300L, CompressionCache.Bytes, "antes");

                CompressionCache.Clear();
                Check.Equal(0, CompressionCache.Count, "entradas");
                Check.Equal(0L, CompressionCache.Bytes, "bytes");
            });

            runner.Test("al superar el techo se vacia y no crece sin limite", delegate
            {
                CompressionCache.Clear();

                // Entradas de 7 MB: cinco superan el techo de 32 MB.
                for (int i = 0; i < 5; i++) CompressionCache.Store("grande-" + i, new byte[7 * 1024 * 1024]);

                Check.True(CompressionCache.Bytes <= 32L * 1024 * 1024, "no supera el techo");
                Check.True(CompressionCache.Count < 5, "hubo vaciado");
                CompressionCache.Clear();
            });

            runner.Test("la misma clave no se duplica", delegate
            {
                CompressionCache.Clear();
                CompressionCache.Store("repetida", new byte[10]);
                CompressionCache.Store("repetida", new byte[10]);
                Check.Equal(1, CompressionCache.Count, "una sola entrada");
                Check.Equal(10L, CompressionCache.Bytes, "no se contabiliza dos veces");
                CompressionCache.Clear();
            });
        }
    }
}
