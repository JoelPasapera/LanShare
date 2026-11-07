using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// CRC-32 y escritura de ZIP. Lo escrito se lee con ZipArchive, de .NET,
    /// que no comparte ni una linea con el escritor: si lo entiende, lo
    /// entiende cualquier descompresor.
    /// </summary>
    public static class ZipTests
    {
        private static readonly DateTime Moment = new DateTime(2026, 10, 16, 6, 30, 14, DateTimeKind.Utc);

        /// <summary>CRC bit a bit, sin tablas: la referencia contra la que se mide la version rapida.</summary>
        private static uint ReferenceCrc(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }

        private static byte[] Bytes(int count, int seed)
        {
            byte[] data = new byte[count];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static Dictionary<string, byte[]> ReadBack(byte[] zip)
        {
            Dictionary<string, byte[]> entries = new Dictionary<string, byte[]>();
            using (ZipArchive archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    using (Stream stream = entry.Open())
                    using (MemoryStream copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        entries[entry.FullName] = copy.ToArray();
                    }
                }
            }
            return entries;
        }

        private static byte[] Build(bool forceZip64, params KeyValuePair<string, byte[]>[] files)
        {
            using (MemoryStream output = new MemoryStream())
            {
                ZipWriter zip = new ZipWriter(output, forceZip64);
                zip.AddDirectoryAsync("raiz", Moment).GetAwaiter().GetResult();
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    zip.AddFileAsync(file.Key, new ChunkedStream(file.Value, 1000), file.Value.Length, Moment)
                       .GetAwaiter().GetResult();
                }
                zip.FinishAsync().GetAwaiter().GetResult();
                return output.ToArray();
            }
        }

        /// <summary>Flujo que entrega unos bytes y despues falla como un disco que se desconecta.</summary>
        private sealed class FailingStream : Stream
        {
            private readonly byte[] _data;
            private int _position;

            public FailingStream(byte[] data)
            {
                _data = data;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }

            public override long Position
            {
                get { return _position; }
                set { throw new NotSupportedException(); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _data.Length) throw new IOException("El dispositivo no esta listo.");
                int n = Math.Min(count, _data.Length - _position);
                Buffer.BlockCopy(_data, _position, buffer, offset, n);
                _position += n;
                return n;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        /// <summary>
        /// Crea una union (junction) con mklink, que no exige administrador.
        /// false si el sistema no lo permite: la prueba se da entonces por
        /// cubierta sin comprobar nada, porque no hay nada que probar.
        /// </summary>
        private static bool TryCreateJunction(string link, string target)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(
                    "cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(10000);
                }
                return Directory.Exists(link);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Dictionary<string, byte[]> ArchiveOf(string directory, string root, out FolderArchive.Result result)
        {
            using (MemoryStream output = new MemoryStream())
            {
                result = FolderArchive.WriteAsync(directory, "web", PathGuard.CanonicalizeRoot(root), output)
                                      .GetAwaiter().GetResult();
                return ReadBack(output.ToArray());
            }
        }

        private static KeyValuePair<string, byte[]> File(string name, byte[] data)
        {
            return new KeyValuePair<string, byte[]>(name, data);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / Crc32");

            runner.Test("vectores conocidos", delegate
            {
                Check.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789"), 0, 9), "123456789");
                Check.Equal(0u, Crc32.Compute(new byte[0], 0, 0), "vacio");
                Check.Equal(0xE8B7BE43u, Crc32.Compute(Encoding.ASCII.GetBytes("a"), 0, 1), "a");
            });

            runner.Test("CLAVE: slicing-by-8 coincide con el calculo bit a bit", delegate
            {
                for (int n = 0; n < 300; n += 7)
                {
                    byte[] data = Bytes(n, n);
                    Check.Equal(ReferenceCrc(data), Crc32.Compute(data, 0, n), "longitud " + n);
                }
            });

            runner.Test("por trozos da lo mismo que de una vez", delegate
            {
                byte[] data = Bytes(10007, 3);
                Crc32 crc = new Crc32();
                int at = 0, step = 1;
                while (at < data.Length)
                {
                    int count = Math.Min(step, data.Length - at);
                    crc.Update(data, at, count);
                    at += count;
                    step = step * 3 % 97 + 1;
                }
                Check.Equal(Crc32.Compute(data, 0, data.Length), crc.Value, "troceado");
            });

            runner.Suite("Core / ZipWriter");

            runner.Test("CLAVE: ZipArchive lee lo escrito, byte a byte", delegate
            {
                byte[] foto = Bytes(150000, 1);
                byte[] texto = Encoding.UTF8.GetBytes("hola\n");
                Dictionary<string, byte[]> read = ReadBack(Build(false,
                    File("raiz/foto.jpg", foto), File("raiz/sub/nota.txt", texto), File("raiz/vacio.bin", new byte[0])));

                Check.True(read.ContainsKey("raiz/"), "carpeta");
                Check.BytesEqual(foto, read["raiz/foto.jpg"], "binario");
                Check.BytesEqual(texto, read["raiz/sub/nota.txt"], "texto en subcarpeta");
                Check.Equal(0, read["raiz/vacio.bin"].Length, "archivo vacio");
            });

            runner.Test("nombres en UTF-8", delegate
            {
                byte[] data = Encoding.UTF8.GetBytes("x");
                Dictionary<string, byte[]> read = ReadBack(Build(false, File("raiz/caf\u00e9 \u00f1 \u65e5\u672c.txt", data)));
                Check.True(read.ContainsKey("raiz/caf\u00e9 \u00f1 \u65e5\u672c.txt"), "nombre intacto");
            });

            runner.Test("ZIP64 forzado tambien se lee", delegate
            {
                byte[] data = Bytes(5000, 2);
                Dictionary<string, byte[]> read = ReadBack(Build(true, File("raiz/a.bin", data)));
                Check.BytesEqual(data, read["raiz/a.bin"], "contenido");
            });

            runner.Test("un archivo que encoge o crece mientras se lee deja un ZIP coherente", delegate
            {
                byte[] data = Bytes(600, 4);
                using (MemoryStream output = new MemoryStream())
                {
                    ZipWriter zip = new ZipWriter(output);
                    long shrunk = zip.AddFileAsync("encoge.bin", new MemoryStream(data), 1000, Moment).GetAwaiter().GetResult();
                    long grown = zip.AddFileAsync("crece.bin", new MemoryStream(data), 100, Moment).GetAwaiter().GetResult();
                    zip.FinishAsync().GetAwaiter().GetResult();

                    Check.Equal(600L, shrunk, "lo que habia");
                    Check.Equal(100L, grown, "solo lo declarado");
                    Dictionary<string, byte[]> read = ReadBack(output.ToArray());
                    Check.Equal(600, read["encoge.bin"].Length, "encoge");
                    Check.Equal(100, read["crece.bin"].Length, "crece");
                }
            });

            runner.Test("la fecha se conserva con la resolucion de MS-DOS", delegate
            {
                byte[] zip = Build(false, File("raiz/t.txt", new byte[] { 1 }));
                using (ZipArchive archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                {
                    DateTime local = Moment.ToLocalTime();
                    DateTime read = archive.GetEntry("raiz/t.txt").LastWriteTime.DateTime;
                    Check.True(Math.Abs((read - local).TotalSeconds) <= 2, "dentro de 2 s: " + read + " frente a " + local);
                }
            });

            runner.Test("SEGURIDAD: los nombres no pueden salir de la carpeta de destino", delegate
            {
                Check.Equal("etc/passwd", ZipWriter.CleanName("../../etc/passwd"), "subidas");
                Check.Equal("abs/x", ZipWriter.CleanName("/abs/x"), "absoluta");
                Check.Equal("a/b/c.txt", ZipWriter.CleanName("a\\b\\c.txt"), "barras invertidas");
                Check.Equal("x/y", ZipWriter.CleanName("./x/./y"), "puntos");
                Check.Equal("dir/", ZipWriter.CleanName("dir/"), "barra final de carpeta");
                Check.Throws(delegate { ZipWriter.CleanName("../"); }, "nada util");
                Check.Throws(delegate { ZipWriter.CleanName(""); }, "vacio");
            });

            runner.Test("REGRESION: un fallo de lectura a mitad deja un ZIP valido", delegate
            {
                byte[] data = Bytes(5000, 7);
                using (MemoryStream output = new MemoryStream())
                {
                    ZipWriter zip = new ZipWriter(output);
                    long written = zip.AddFileAsync("roto.bin", new FailingStream(data), 9000, Moment).GetAwaiter().GetResult();
                    zip.AddFileAsync("sano.txt", new MemoryStream(new byte[] { 1, 2, 3 }), 3, Moment).GetAwaiter().GetResult();
                    zip.FinishAsync().GetAwaiter().GetResult();

                    Check.Equal(5000L, written, "lo que llego a leerse");
                    Dictionary<string, byte[]> read = ReadBack(output.ToArray());
                    Check.BytesEqual(data, read["roto.bin"], "la parte leida, intacta");
                    Check.Equal(3, read["sano.txt"].Length, "lo siguiente sigue ahi");
                }
            });

            runner.Test("cerrar dos veces o anadir tras cerrar falla", delegate
            {
                ZipWriter zip = new ZipWriter(new MemoryStream());
                zip.FinishAsync().GetAwaiter().GetResult();
                Check.Throws(delegate { zip.FinishAsync().GetAwaiter().GetResult(); }, "doble cierre");
                Check.Throws(delegate { zip.AddDirectoryAsync("x", Moment).GetAwaiter().GetResult(); }, "anadir tras cerrar");
            });

            runner.Test("un ZIP vacio es valido", delegate
            {
                using (MemoryStream output = new MemoryStream())
                {
                    new ZipWriter(output).FinishAsync().GetAwaiter().GetResult();
                    Check.Equal(22L, output.Length, "solo el registro de cierre");
                    Check.Equal(0, ReadBack(output.ToArray()).Count, "sin entradas");
                }
            });

            runner.Suite("Core / FolderArchive");

            runner.Test("empaqueta el arbol bajo una carpeta con su nombre", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("web/index.html", "<p>hola</p>");
                    tree.File("web/css/app.css", "body{}");
                    tree.Dir("web/vacia");

                    using (MemoryStream output = new MemoryStream())
                    {
                        string web = Path.Combine(tree.Root, "web");
                        FolderArchive.Result result = FolderArchive.WriteAsync(web, "web", PathGuard.CanonicalizeRoot(web), output)
                                                                   .GetAwaiter().GetResult();
                        Check.Equal(2, result.Files, "dos archivos");
                        Check.Equal(3, result.Directories, "raiz, css y vacia");
                        Check.Equal(17L, result.Bytes, "bytes de contenido");

                        Dictionary<string, byte[]> read = ReadBack(output.ToArray());
                        Check.Equal("<p>hola</p>", Encoding.UTF8.GetString(read["web/index.html"]), "contenido");
                        Check.True(read.ContainsKey("web/css/app.css"), "subcarpeta");
                        Check.True(read.ContainsKey("web/vacia/"), "carpeta vacia conservada");
                    }
                }
            });

            runner.Test("SEGURIDAD: sin ocultos ni archivos de sistema, como el listado", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("web/visible.txt", "a");
                    string hidden = tree.File("web/secreto.txt", "b");
                    System.IO.File.SetAttributes(hidden, FileAttributes.Hidden);

                    using (MemoryStream output = new MemoryStream())
                    {
                        string web = Path.Combine(tree.Root, "web");
                        FolderArchive.WriteAsync(web, "web", PathGuard.CanonicalizeRoot(web), output).GetAwaiter().GetResult();
                        Dictionary<string, byte[]> read = ReadBack(output.ToArray());
                        Check.True(read.ContainsKey("web/visible.txt"), "visible");
                        Check.False(read.ContainsKey("web/secreto.txt"), "oculto fuera");
                    }
                    System.IO.File.SetAttributes(hidden, FileAttributes.Normal);
                }
            });

            runner.Test("SEGURIDAD: una carpeta fuera de la raiz no se empaqueta", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string sitio = tree.Dir("sitio");
                    string otro = tree.Dir("otro");
                    tree.File("otro/secreto.txt", "x");
                    MemoryStream output = new MemoryStream();
                    Check.Throws(delegate
                    {
                        FolderArchive.WriteAsync(otro, "otro", PathGuard.CanonicalizeRoot(sitio), output)
                                     .GetAwaiter().GetResult();
                    }, "rechazada");
                    Check.Equal(0L, output.Length, "sin escribir ni un byte");
                }
            });

            runner.Test("una etiqueta inutil como nombre de la carpeta raiz da 'carpeta'", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string web = tree.Dir("web");
                    tree.File("web/a.txt", "a");
                    using (MemoryStream output = new MemoryStream())
                    {
                        FolderArchive.WriteAsync(web, "..", PathGuard.CanonicalizeRoot(web), output).GetAwaiter().GetResult();
                        Check.True(ReadBack(output.ToArray()).ContainsKey("carpeta/a.txt"), "carpeta por defecto");
                    }
                }
            });

            runner.Test("SEGURIDAD: solo se empaqueta lo que esta dentro de la raiz real", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string root = tree.Dir("sitio");
                    string realRoot = PathGuard.CanonicalizeRoot(root);
                    Check.True(PathGuard.DirectoryInsideRoot(tree.Dir("sitio/fotos"), realRoot), "subcarpeta");
                    Check.True(PathGuard.DirectoryInsideRoot(root, realRoot), "la propia raiz");
                    Check.False(PathGuard.DirectoryInsideRoot(tree.Dir("otro"), realRoot), "carpeta hermana");
                    Check.False(PathGuard.DirectoryInsideRoot(tree.Dir("sitio2"), realRoot), "mismo prefijo, otra carpeta");
                    Check.False(PathGuard.DirectoryInsideRoot(Path.Combine(tree.Root, "no-existe"), realRoot), "inexistente");
                    Check.False(PathGuard.DirectoryInsideRoot(root, ""), "sin raiz");
                }
            });

            runner.Test("SEGURIDAD: una union que sale del sitio se queda fuera del ZIP", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string web = tree.Dir("web");
                    tree.File("web/a.txt", "a");
                    tree.File("fuera/secreto.txt", "s");
                    if (!TryCreateJunction(Path.Combine(web, "enlace"), Path.Combine(tree.Root, "fuera"))) return;

                    FolderArchive.Result result;
                    Dictionary<string, byte[]> read = ArchiveOf(web, web, out result);
                    Check.True(read.ContainsKey("web/a.txt"), "lo de dentro");
                    Check.False(read.ContainsKey("web/enlace/secreto.txt"), "lo de fuera no");
                    Check.True(result.Skipped >= 1, "contado como omitido");
                    Directory.Delete(Path.Combine(web, "enlace"));
                }
            });

            runner.Test("SEGURIDAD: una union a una carpeta superior no forma un bucle", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string web = tree.Dir("web");
                    tree.File("web/sub/a.txt", "a");
                    if (!TryCreateJunction(Path.Combine(web, "sub", "arriba"), web)) return;

                    FolderArchive.Result result;
                    Dictionary<string, byte[]> read = ArchiveOf(web, web, out result);
                    Check.True(read.ContainsKey("web/sub/a.txt"), "el contenido real");
                    Check.False(read.ContainsKey("web/sub/arriba/sub/a.txt"), "sin entrar en el bucle");
                    Check.False(result.Truncated, "termina sin agotar el tope");
                    Directory.Delete(Path.Combine(web, "sub", "arriba"));
                }
            });

            runner.Test("una union a una hermana no le quita su nombre real", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string web = tree.Dir("web");
                    tree.File("web/fotos/f.jpg", "f");

                    // "aa" va antes que "fotos" en el orden del disco: sin reclamar
                    // primero las carpetas de verdad, el alias se quedaria con ella.
                    if (!TryCreateJunction(Path.Combine(web, "aa"), Path.Combine(web, "fotos"))) return;

                    FolderArchive.Result result;
                    Dictionary<string, byte[]> read = ArchiveOf(web, web, out result);
                    Check.True(read.ContainsKey("web/fotos/f.jpg"), "con su nombre real");
                    Check.False(read.ContainsKey("web/aa/f.jpg"), "el alias no se repite");
                    Directory.Delete(Path.Combine(web, "aa"));
                }
            });

            runner.Test("la consulta ?zip se reconoce sin confundirse", delegate
            {
                Check.True(FolderArchive.IsRequested("?zip"), "?zip");
                Check.True(FolderArchive.IsRequested("?ZIP=1"), "mayusculas con valor");
                Check.True(FolderArchive.IsRequested("?a=1&zip"), "entre otros");
                Check.False(FolderArchive.IsRequested("?zipper"), "otra palabra");
                Check.False(FolderArchive.IsRequested("?a=zip"), "como valor");
                Check.False(FolderArchive.IsRequested(""), "vacia");
                Check.False(FolderArchive.IsRequested(null), "nula");
            });

            runner.Suite("Core / ContentDisposition");

            runner.Test("nombre ASCII de repuesto y nombre real en UTF-8", delegate
            {
                Check.Equal("attachment; filename=\"fotos.zip\"; filename*=UTF-8''fotos.zip",
                            ContentDisposition.Attachment("fotos.zip"), "ASCII");
                Check.Equal("attachment; filename=\"caf_ _.zip\"; filename*=UTF-8''caf%C3%A9%20%C3%B1.zip",
                            ContentDisposition.Attachment("caf\u00e9 \u00f1.zip"), "acentos");
            });

            runner.Test("SEGURIDAD: comillas y saltos de linea no rompen la cabecera", delegate
            {
                string header = ContentDisposition.Attachment("a\"b\r\nSet-Cookie: x.zip");
                Check.DoesNotContain(header, "\r", "sin retorno de carro");
                Check.DoesNotContain(header, "\n", "sin salto de linea");
                Check.Contains(header, "filename=\"a_b__Set-Cookie: x.zip\"", "repuesto saneado");
                Check.Contains(header, "a%22b%0D%0ASet-Cookie%3A%20x.zip", "real codificado");
            });
        }
    }
}
