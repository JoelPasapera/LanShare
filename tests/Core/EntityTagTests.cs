using System;
using System.IO;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Validadores de cache. Un fallo aqui hace que el cliente reutilice
    /// contenido viejo, o peor: bytes comprimidos como si fueran texto plano.
    /// </summary>
    public static class EntityTagTests
    {
        private static readonly DateTime Momento = new DateTime(2026, 9, 15, 12, 30, 45, DateTimeKind.Utc);

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / EntityTag");

            runner.Test("el validador es estable y va entrecomillado", delegate
            {
                string a = EntityTag.Build(1000, Momento, "");
                string b = EntityTag.Build(1000, Momento, "");
                Check.Equal(a, b, "determinista");
                Check.StartsWith(a, "\"", "abre comillas");
                Check.True(a.EndsWith("\"", StringComparison.Ordinal), "cierra comillas");
            });

            runner.Test("cambiar tamano o fecha cambia el validador", delegate
            {
                string baseTag = EntityTag.Build(1000, Momento, "");
                Check.NotEqual(baseTag, EntityTag.Build(1001, Momento, ""), "otro tamano");
                Check.NotEqual(baseTag, EntityTag.Build(1000, Momento.AddSeconds(1), ""), "otra fecha");
            });

            runner.Test("CRITICO: cada representacion tiene validador propio", delegate
            {
                // Sin esto, un cliente podria reutilizar el cuerpo gzip como
                // si fuera el texto plano del mismo recurso.
                string plano = EntityTag.Build(1000, Momento, "");
                string gzip = EntityTag.Build(1000, Momento, "g");
                string sonda = EntityTag.Build(1000, Momento, "i");
                string ambos = EntityTag.Build(1000, Momento, "ig");

                Check.NotEqual(plano, gzip, "plano frente a gzip");
                Check.NotEqual(plano, sonda, "plano frente a sonda");
                Check.NotEqual(gzip, sonda, "gzip frente a sonda");
                Check.NotEqual(ambos, gzip, "ambos frente a gzip");
                Check.NotEqual(ambos, sonda, "ambos frente a sonda");
            });

            runner.Test("la fecha se trunca a segundos", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string file = tree.File("a.txt", "contenido");
                    DateTime leida = EntityTag.GetLastWriteUtc(file);

                    Check.Equal(0, leida.Millisecond, "sin milisegundos");
                    Check.Equal(DateTimeKind.Utc, leida.Kind, "en UTC");
                }
            });

            runner.Test("REGRESION: archivo inexistente devuelve fecha minima", delegate
            {
                // Windows no lanza al pedir la fecha de un archivo que no
                // existe: devuelve la epoca de FILETIME (1601). Dejar pasar
                // ese valor hacia que el servidor respondiera 304 a cualquier
                // If-Modified-Since.
                Check.Equal(DateTime.MinValue,
                            EntityTag.GetLastWriteUtc(@"C:\no-existe-" + Guid.NewGuid().ToString("N")),
                            "sin fecha");
            });

            runner.Test("REGRESION: archivo borrado mientras se sirve", delegate
            {
                // Se abre con FILE_SHARE_DELETE, asi que un editor puede
                // borrarlo y recrearlo mientras esta en vuelo.
                string ruta;
                using (TempTree tree = new TempTree())
                {
                    ruta = tree.File("efimero.txt", "contenido");
                    Check.NotEqual(DateTime.MinValue, EntityTag.GetLastWriteUtc(ruta), "mientras existe");
                }

                Check.Equal(DateTime.MinValue, EntityTag.GetLastWriteUtc(ruta), "tras desaparecer");
            });

            runner.Test("REGRESION: sin fecha conocida nunca se declara copia vigente", delegate
            {
                // Esta es la consecuencia que importaba: con 1601 en lugar de
                // fecha minima, la comparacion daba verdadero siempre.
                string tag = EntityTag.Build(1000, DateTime.MinValue, "");

                Check.False(EntityTag.ClientHasFreshCopy(null, EntityTag.ToHttpDate(Momento),
                                                         tag, DateTime.MinValue),
                            "fecha reciente del cliente");
                Check.False(EntityTag.ClientHasFreshCopy(null, EntityTag.ToHttpDate(Momento.AddYears(50)),
                                                         tag, DateTime.MinValue),
                            "fecha futura del cliente");
                Check.False(EntityTag.ClientHasFreshCopy(null, "Thu, 01 Jan 1970 00:00:00 GMT",
                                                         tag, DateTime.MinValue),
                            "fecha antigua del cliente");
            });

            runner.Test("una fecha real nunca cae en el centinela", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    DateTime leida = EntityTag.GetLastWriteUtc(tree.File("a.txt", "x"));
                    Check.True(leida.Year >= 2000, "fecha plausible, no 1601");
                }
            });

            runner.Test("If-None-Match coincidente da copia vigente", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                Check.True(EntityTag.ClientHasFreshCopy(tag, null, tag, Momento), "mismo validador");
            });

            runner.Test("If-None-Match distinto obliga a reenviar", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                string otro = EntityTag.Build(2000, Momento, "");
                Check.False(EntityTag.ClientHasFreshCopy(otro, null, tag, Momento), "otro validador");
            });

            runner.Test("el comodin siempre coincide", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                Check.True(EntityTag.ClientHasFreshCopy("*", null, tag, Momento), "asterisco");
            });

            runner.Test("se acepta una lista y el prefijo debil", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                Check.True(EntityTag.ClientHasFreshCopy("\"otro\", " + tag, null, tag, Momento), "lista");
                Check.True(EntityTag.ClientHasFreshCopy("W/" + tag, null, tag, Momento), "prefijo W/");
            });

            runner.Test("If-Modified-Since posterior da copia vigente", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                string despues = EntityTag.ToHttpDate(Momento.AddMinutes(5));
                Check.True(EntityTag.ClientHasFreshCopy(null, despues, tag, Momento), "posterior");
            });

            runner.Test("If-Modified-Since anterior obliga a reenviar", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                string antes = EntityTag.ToHttpDate(Momento.AddMinutes(-5));
                Check.False(EntityTag.ClientHasFreshCopy(null, antes, tag, Momento), "anterior");
            });

            runner.Test("misma fecha exacta cuenta como vigente", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                Check.True(EntityTag.ClientHasFreshCopy(null, EntityTag.ToHttpDate(Momento), tag, Momento),
                           "igual");
            });

            runner.Test("If-None-Match manda sobre If-Modified-Since", delegate
            {
                // RFC 7232: si llegan los dos, gana el validador fuerte.
                string tag = EntityTag.Build(1000, Momento, "");
                string otro = EntityTag.Build(2000, Momento, "");
                string futuro = EntityTag.ToHttpDate(Momento.AddYears(1));

                Check.False(EntityTag.ClientHasFreshCopy(otro, futuro, tag, Momento),
                            "el validador no coincide pese a la fecha futura");
            });

            runner.Test("fecha ilegible no da falso positivo", delegate
            {
                string tag = EntityTag.Build(1000, Momento, "");
                Check.False(EntityTag.ClientHasFreshCopy(null, "no es una fecha", tag, Momento), "basura");
            });

            runner.Test("la fecha HTTP sale en formato RFC 1123", delegate
            {
                string fecha = EntityTag.ToHttpDate(Momento);
                Check.Contains(fecha, "GMT", "zona");
                Check.Contains(fecha, "2026", "ano");
            });
        }
    }
}
