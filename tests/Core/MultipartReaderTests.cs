using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Analizador multipart en flujo. Cada caso se repite con lecturas de
    /// distintos tamanos: un fallo de contabilidad en el bufer solo aparece
    /// cuando el delimitador cae partido justo en el sitio equivocado.
    /// </summary>
    public static class MultipartReaderTests
    {
        private const string Boundary = "----WebKitFormBoundaryLanShare7Mz";
        private static readonly int[] Chunks = { 1, 2, 3, 7, 64, 4096, 70000 };

        private sealed class Part
        {
            public string Headers;
            public byte[] Body;
            public Part(string headers, byte[] body) { Headers = headers; Body = body; }
        }

        private static byte[] Build(IList<Part> parts, string preamble, string epilogue)
        {
            MemoryStream ms = new MemoryStream();
            Action<string> text = delegate(string s)
            {
                byte[] b = Encoding.UTF8.GetBytes(s);
                ms.Write(b, 0, b.Length);
            };

            if (preamble.Length > 0) text(preamble + "\r\n");
            foreach (Part part in parts)
            {
                text("--" + Boundary + "\r\n");
                if (part.Headers.Length > 0) text(part.Headers + "\r\n");
                text("\r\n");
                ms.Write(part.Body, 0, part.Body.Length);
                text("\r\n");
            }
            text("--" + Boundary + "--" + epilogue);
            return ms.ToArray();
        }

        private static string FileHeaders(string field, string name)
        {
            return "Content-Disposition: form-data; name=\"" + field + "\"; filename=\"" + name + "\"\r\n" +
                   "Content-Type: application/octet-stream";
        }

        private static byte[] RandomBytes(int size, int seed)
        {
            byte[] data = new byte[size];
            new Random(seed).NextBytes(data);
            return data;
        }

        /// <summary>Lee todo y devuelve las partes con su cuerpo.</summary>
        private static List<KeyValuePair<MultipartPart, byte[]>> ReadAll(byte[] payload, int chunk, long limit)
        {
            MultipartReader reader = new MultipartReader(new ChunkedStream(payload, chunk), Boundary);
            List<KeyValuePair<MultipartPart, byte[]>> result = new List<KeyValuePair<MultipartPart, byte[]>>();

            while (true)
            {
                MultipartPart part = reader.ReadNextPartAsync().GetAwaiter().GetResult();
                if (part == null) break;

                MemoryStream body = new MemoryStream();
                reader.CopyBodyAsync(body, limit).GetAwaiter().GetResult();
                result.Add(new KeyValuePair<MultipartPart, byte[]>(part, body.ToArray()));
            }
            return result;
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / MultipartReader");

            runner.Test("un archivo vuelve intacto con cualquier troceo", delegate
            {
                byte[] body = RandomBytes(5000, 1);
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("archivo", "foto.jpg"), body));
                byte[] payload = Build(parts, "", "");

                foreach (int chunk in Chunks)
                {
                    List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(payload, chunk, long.MaxValue);
                    Check.Equal(1, got.Count, "una parte con trozos de " + chunk);
                    Check.Equal("foto.jpg", got[0].Key.FileName, "nombre con trozos de " + chunk);
                    Check.Equal("archivo", got[0].Key.Name, "campo con trozos de " + chunk);
                    Check.BytesEqual(body, got[0].Value, "cuerpo con trozos de " + chunk);
                }
            });

            runner.Test("varias partes: texto y dos archivos", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part("Content-Disposition: form-data; name=\"comentario\"", Encoding.UTF8.GetBytes("hola")));
                parts.Add(new Part(FileHeaders("archivo", "a.txt"), Encoding.UTF8.GetBytes("primero")));
                parts.Add(new Part(FileHeaders("archivo", "b.bin"), RandomBytes(70000, 2)));
                byte[] payload = Build(parts, "", "");

                foreach (int chunk in new int[] { 3, 4096 })
                {
                    List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(payload, chunk, long.MaxValue);
                    Check.Equal(3, got.Count, "tres partes");
                    Check.False(got[0].Key.IsFile, "el comentario no es un archivo");
                    Check.Equal("hola", Encoding.UTF8.GetString(got[0].Value), "texto del campo");
                    Check.True(got[1].Key.IsFile, "segundo es archivo");
                    Check.BytesEqual(parts[2].Body, got[2].Value, "archivo grande intacto");
                }
            });

            runner.Test("cuerpos traicioneros: casi-delimitadores y saltos de linea", delegate
            {
                byte[] trampa = Encoding.ASCII.GetBytes("\r\n--" + Boundary.Substring(0, Boundary.Length - 1) + "X\r\n");
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "1"), trampa));
                parts.Add(new Part(FileHeaders("f", "2"), Encoding.ASCII.GetBytes("\r\n\r\nempieza como fin de cabecera")));
                parts.Add(new Part(FileHeaders("f", "3"), Encoding.ASCII.GetBytes("acaba en salto\r\n")));
                parts.Add(new Part(FileHeaders("f", "4"), new byte[0]));
                byte[] payload = Build(parts, "", "");

                foreach (int chunk in Chunks)
                {
                    List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(payload, chunk, long.MaxValue);
                    Check.Equal(4, got.Count, "cuatro partes con trozos de " + chunk);
                    for (int i = 0; i < 4; i++)
                        Check.BytesEqual(parts[i].Body, got[i].Value, "parte " + (i + 1) + " con trozos de " + chunk);
                }
            });

            runner.Test("preambulo y epilogo se ignoran", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "x"), Encoding.ASCII.GetBytes("dato")));
                byte[] payload = Build(parts, "Esto es un preambulo que nadie lee", "\r\nY un epilogo");

                List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(payload, 5, long.MaxValue);
                Check.Equal(1, got.Count, "una parte");
                Check.Equal("dato", Encoding.ASCII.GetString(got[0].Value), "cuerpo");
            });

            runner.Test("parte sin cabeceras", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part("", Encoding.ASCII.GetBytes("sin cabeceras")));
                List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(Build(parts, "", ""), 2, long.MaxValue);

                Check.Equal(1, got.Count, "una parte");
                Check.False(got[0].Key.IsFile, "no es archivo");
                Check.Equal("sin cabeceras", Encoding.ASCII.GetString(got[0].Value), "cuerpo");
            });

            runner.Test("relleno de espacios tras el delimitador (RFC 2046)", delegate
            {
                string raw = "--" + Boundary + "  \t\r\nContent-Disposition: form-data; name=\"a\"\r\n\r\nvalor\r\n--" +
                             Boundary + "--";
                List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(Encoding.ASCII.GetBytes(raw), 3, long.MaxValue);
                Check.Equal(1, got.Count, "una parte");
                Check.Equal("valor", Encoding.ASCII.GetString(got[0].Value), "cuerpo");
            });

            runner.Test("una parte no consumida se descarta sola", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "ignorada"), RandomBytes(9000, 3)));
                parts.Add(new Part(FileHeaders("f", "leida"), Encoding.ASCII.GetBytes("la segunda")));
                MultipartReader reader = new MultipartReader(new ChunkedStream(Build(parts, "", ""), 7), Boundary);

                MultipartPart first = reader.ReadNextPartAsync().GetAwaiter().GetResult();
                Check.Equal("ignorada", first.FileName, "primera");

                MultipartPart second = reader.ReadNextPartAsync().GetAwaiter().GetResult();
                Check.Equal("leida", second.FileName, "segunda tras saltar el cuerpo de la primera");

                MemoryStream body = new MemoryStream();
                reader.CopyBodyAsync(body, long.MaxValue).GetAwaiter().GetResult();
                Check.Equal("la segunda", Encoding.ASCII.GetString(body.ToArray()), "cuerpo de la segunda");
                Check.Null(reader.ReadNextPartAsync().GetAwaiter().GetResult(), "fin");
            });

            runner.Test("LIMITE: un byte de mas lanza, el tamano exacto no", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "x"), Encoding.ASCII.GetBytes("0123456789")));
                byte[] payload = Build(parts, "", "");

                Check.DoesNotThrow(delegate { ReadAll(payload, 3, 10); }, "exactamente en el limite");
                Check.Throws(delegate { ReadAll(payload, 3, 9); }, "un byte por encima");

                try
                {
                    ReadAll(payload, 3, 9);
                }
                catch (MultipartLimitException) { return; }
                throw new AssertionException("se esperaba precisamente MultipartLimitException");
            });

            runner.Test("entrada truncada lanza en vez de inventar un final", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "x"), RandomBytes(3000, 4)));
                byte[] full = Build(parts, "", "");
                byte[] cut = new byte[full.Length - 40];
                Buffer.BlockCopy(full, 0, cut, 0, cut.Length);

                Check.Throws(delegate { ReadAll(cut, 64, long.MaxValue); }, "cortada a mitad del cuerpo");
            });

            runner.Test("sin ningun delimitador lanza", delegate
            {
                Check.Throws(delegate { ReadAll(Encoding.ASCII.GetBytes("no hay nada aqui"), 4, long.MaxValue); },
                             "basura");
                Check.Throws(delegate { ReadAll(new byte[0], 4, long.MaxValue); }, "vacio");
            });

            runner.Test("delimitador invalido se rechaza al construir", delegate
            {
                Check.Throws(delegate { new MultipartReader(new MemoryStream(), ""); }, "vacio");
                Check.Throws(delegate { new MultipartReader(new MemoryStream(), new string('a', 201)); }, "demasiado largo");
                Check.Throws(delegate { new MultipartReader(null, "x"); }, "flujo nulo");
            });

            runner.Test("un archivo de 1 MB atraviesa un bufer de 64 KB", delegate
            {
                byte[] body = RandomBytes(1024 * 1024, 5);
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "grande.bin"), body));
                List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(Build(parts, "", ""), 65536, long.MaxValue);
                Check.BytesEqual(body, got[0].Value, "intacto");
            });

            runner.Suite("Core / MultipartReader - cabeceras");

            runner.Test("delimitador desde Content-Type", delegate
            {
                string b;
                Check.True(MultipartReader.TryGetBoundary("multipart/form-data; boundary=abc123", out b), "sin comillas");
                Check.Equal("abc123", b, "valor");
                Check.True(MultipartReader.TryGetBoundary("multipart/form-data; boundary=\"con espacio\"", out b), "con comillas");
                Check.Equal("con espacio", b, "valor entre comillas");
                Check.True(MultipartReader.TryGetBoundary("Multipart/Form-Data;BOUNDARY=x", out b), "mayusculas");
                Check.False(MultipartReader.TryGetBoundary("application/json", out b), "otro tipo");
                Check.False(MultipartReader.TryGetBoundary("multipart/form-data", out b), "sin delimitador");
                Check.False(MultipartReader.TryGetBoundary(null, out b), "nulo");
            });

            runner.Test("Content-Disposition con nombre y archivo", delegate
            {
                string name, file;
                MultipartReader.ParseContentDisposition("form-data; name=\"archivo\"; filename=\"foto 1.jpg\"",
                                                        out name, out file);
                Check.Equal("archivo", name, "campo");
                Check.Equal("foto 1.jpg", file, "archivo");
            });

            runner.Test("el punto y coma dentro de comillas no separa", delegate
            {
                string name, file;
                MultipartReader.ParseContentDisposition("form-data; name=\"a\"; filename=\"x;y=z.txt\"", out name, out file);
                Check.Equal("x;y=z.txt", file, "nombre con punto y coma");
            });

            runner.Test("filename* (RFC 5987) manda y decodifica UTF-8", delegate
            {
                string name, file;
                MultipartReader.ParseContentDisposition(
                    "form-data; name=\"a\"; filename=\"fallback.txt\"; filename*=UTF-8''a%C3%B1o%20nuevo.txt",
                    out name, out file);
                Check.Equal("a\u00f1o nuevo.txt", file, "nombre extendido decodificado");
            });

            runner.Test("un campo de texto no tiene nombre de archivo", delegate
            {
                string name, file;
                MultipartReader.ParseContentDisposition("form-data; name=\"comentario\"", out name, out file);
                Check.Equal("comentario", name, "campo");
                Check.Null(file, "sin archivo");
            });

            runner.Test("nombres no ASCII en UTF-8 crudo, como los envia Chrome", delegate
            {
                List<Part> parts = new List<Part>();
                parts.Add(new Part(FileHeaders("f", "cami\u00f3n \u00e1rbol.png"), Encoding.ASCII.GetBytes("x")));
                List<KeyValuePair<MultipartPart, byte[]>> got = ReadAll(Build(parts, "", ""), 4, long.MaxValue);
                Check.Equal("cami\u00f3n \u00e1rbol.png", got[0].Key.FileName, "nombre conservado");
            });

            runner.Suite("Core / MultipartReader - Horspool");

            runner.Test("coincide con la busqueda ingenua en 3000 casos aleatorios", delegate
            {
                Random rng = new Random(77);
                byte[] alphabet = Encoding.ASCII.GetBytes("ab\r\n-");

                for (int t = 0; t < 3000; t++)
                {
                    byte[] pattern = new byte[rng.Next(1, 9)];
                    for (int i = 0; i < pattern.Length; i++) pattern[i] = alphabet[rng.Next(alphabet.Length)];
                    byte[] data = new byte[rng.Next(0, 61)];
                    for (int i = 0; i < data.Length; i++) data[i] = alphabet[rng.Next(alphabet.Length)];

                    int from = rng.Next(0, data.Length + 1);
                    int to = rng.Next(from, data.Length + 1);

                    int expected = Naive(data, from, to, pattern);
                    int actual = MultipartReader.IndexOf(data, from, to, pattern, MultipartReader.BuildSkipTable(pattern));
                    if (expected != actual)
                        throw new AssertionException("caso " + t + ": esperado " + expected + ", llego " + actual);
                }
            });
        }

        private static int Naive(byte[] data, int from, int to, byte[] pattern)
        {
            for (int i = from; i + pattern.Length <= to; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }
    }
}
