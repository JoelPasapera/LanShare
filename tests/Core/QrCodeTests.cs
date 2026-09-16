using System;
using System.Collections.Generic;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Generacion de codigos QR, verificada contra un decodificador
    /// independiente. Es lo unico que puede afirmar que un telefono lo leera.
    /// </summary>
    public static class QrCodeTests
    {
        private static void RoundTrip(string texto, int versionEsperada)
        {
            QrCode code = QrCode.Encode(texto);
            QrDecoder.Result result = QrDecoder.Decode(code);

            Check.Equal(versionEsperada, code.Version, "version para " + texto.Length + " caracteres");
            Check.Equal(0, result.FormatDistance, "los bits de formato pertenecen a la tabla BCH");
            Check.True(result.SyndromesZero, "los sindromes Reed-Solomon valen cero");
            Check.Equal(4, result.Mode, "modo byte");
            Check.Equal(texto, result.Text, "el texto vuelve intacto");
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / QrCode - estructura");

            runner.Test("INVARIANTE: el recuento de modulos de servicio cuadra", delegate
            {
                // Este es el calculo que destapo el fallo del reparto de bits de
                // formato: si sobran o faltan modulos, el flujo de datos va
                // desplazado y ningun lector puede interpretarlo.
                for (int version = 1; version <= 6; version++)
                {
                    int size = version * 4 + 17;
                    Check.Equal(QrDecoder.ExpectedFunctionModules(version),
                                QrDecoder.CountFunctionModules(size, version),
                                "modulos de servicio en la version " + version);
                }
            });

            runner.Test("el tamano sigue la formula de la norma", delegate
            {
                int[] esperados = { 0, 21, 25, 29, 33, 37, 41 };
                for (int version = 1; version <= 6; version++)
                {
                    QrCode code = QrCode.Encode(new string('a', VersionPayload(version)));
                    Check.Equal(esperados[code.Version], code.Size, "tamano de la version " + code.Version);
                }
            });

            runner.Test("los tres patrones de busqueda estan en su sitio", delegate
            {
                QrCode code = QrCode.Encode("http://192.168.1.8:8080/");
                int last = code.Size - 1;

                int[][] centros = new int[][] {
                    new int[] { 3, 3 }, new int[] { 3, last - 3 }, new int[] { last - 3, 3 }
                };

                foreach (int[] centro in centros)
                {
                    int r = centro[0], c = centro[1];
                    Check.True(code[r, c], "centro oscuro");
                    Check.True(code[r - 1, c - 1] && code[r + 1, c + 1], "nucleo 3x3 oscuro");
                    Check.False(code[r - 2, c], "anillo claro");
                    Check.True(code[r - 3, c], "anillo exterior oscuro");
                }
            });

            runner.Test("los separadores aislan cada patron de busqueda", delegate
            {
                QrCode code = QrCode.Encode("http://192.168.1.8:8080/");
                for (int i = 0; i <= 7; i++)
                {
                    Check.False(code[7, i], "separador horizontal arriba a la izquierda");
                    Check.False(code[i, 7], "separador vertical arriba a la izquierda");
                }
            });

            runner.Test("la sincronizacion alterna claro y oscuro", delegate
            {
                QrCode code = QrCode.Encode("http://192.168.1.8:8080/");
                for (int i = 8; i < code.Size - 8; i++)
                {
                    Check.Equal(i % 2 == 0, code[6, i], "fila de sincronizacion en " + i);
                    Check.Equal(i % 2 == 0, code[i, 6], "columna de sincronizacion en " + i);
                }
            });

            runner.Test("el modulo oscuro obligatorio esta presente", delegate
            {
                QrCode code = QrCode.Encode("http://192.168.1.8:8080/");
                Check.True(code[code.Size - 8, 8], "modulo oscuro en (size-8, 8)");
            });

            runner.Test("el patron de alineacion aparece desde la version 2", delegate
            {
                QrCode code = QrCode.Encode(new string('a', 30));   // fuerza version 3
                Check.True(code.Version >= 2, "version con alineacion");

                int centro = code.Size - 7;
                Check.True(code[centro, centro], "centro del patron");
                Check.False(code[centro - 1, centro], "anillo claro");
                Check.True(code[centro - 2, centro], "anillo exterior oscuro");
            });

            runner.Suite("Core / QrCode - ida y vuelta");

            runner.Test("URL tipica de red local", delegate
            {
                RoundTrip("http://192.168.1.8:8080/", 2);
            });

            runner.Test("varias URL de red local reales", delegate
            {
                string[] urls = {
                    "http://192.168.1.208:8080/", "http://10.0.0.1:80/",
                    "http://172.16.0.100:3000/", "http://192.168.100.100:65535/",
                    "http://192.168.0.1:5173/"
                };

                foreach (string url in urls)
                {
                    QrCode code = QrCode.Encode(url);
                    QrDecoder.Result result = QrDecoder.Decode(code);
                    Check.Equal(url, result.Text, "ida y vuelta de " + url);
                    Check.True(result.SyndromesZero, "sindromes de " + url);
                    Check.Equal(0, result.FormatDistance, "formato de " + url);
                }
            });

            runner.Test("todas las longitudes de 1 a 106 caracteres", delegate
            {
                for (int length = 1; length <= 106; length++)
                {
                    string texto = new string('u', length);
                    QrCode code = QrCode.Encode(texto);
                    QrDecoder.Result result = QrDecoder.Decode(code);

                    Check.Equal(texto, result.Text, "longitud " + length);
                    Check.True(result.SyndromesZero, "sindromes en longitud " + length);
                    Check.Equal(0, result.FormatDistance, "formato en longitud " + length);
                }
            });

            runner.Test("fronteras de capacidad de cada version", delegate
            {
                RoundTrip(new string('a', 14), 1);
                RoundTrip(new string('a', 15), 2);
                RoundTrip(new string('a', 26), 2);
                RoundTrip(new string('a', 27), 3);
                RoundTrip(new string('a', 42), 3);
                RoundTrip(new string('a', 43), 4);
                RoundTrip(new string('a', 62), 4);
                RoundTrip(new string('a', 63), 5);
                RoundTrip(new string('a', 84), 5);
                RoundTrip(new string('a', 85), 6);
                RoundTrip(new string('a', 106), 6);
            });

            runner.Test("se rechaza lo que no cabe", delegate
            {
                Check.Throws(delegate { QrCode.Encode(new string('a', 107)); }, "107 caracteres");
                Check.Throws(delegate { QrCode.Encode(new string('a', 500)); }, "500 caracteres");
            });

            runner.Test("texto nulo lanza", delegate
            {
                Check.Throws(delegate { QrCode.Encode(null); }, "nulo");
            });

            runner.Test("acentos y caracteres de varios bytes", delegate
            {
                string[] textos = { "cafe-n-http://192.168.1.8:8080/", "anos", "\u00F1\u00E1\u00E9\u00ED" };
                foreach (string texto in textos)
                {
                    QrCode code = QrCode.Encode(texto);
                    QrDecoder.Result result = QrDecoder.Decode(code);
                    Check.Equal(texto, result.Text, "UTF-8 en " + texto);
                    Check.True(result.SyndromesZero, "sindromes en " + texto);
                }
            });

            runner.Test("un solo caracter", delegate
            {
                RoundTrip("A", 1);
            });

            runner.Test("el nivel de correccion declarado es M", delegate
            {
                QrDecoder.Result result = QrDecoder.Decode(QrCode.Encode("http://192.168.1.8:8080/"));
                Check.Equal(0, result.ErrorCorrectionLevel, "nivel M se codifica como 0");
            });

            runner.Test("la mascara elegida esta en el rango valido", delegate
            {
                for (int length = 1; length <= 60; length += 7)
                {
                    QrDecoder.Result result = QrDecoder.Decode(QrCode.Encode(new string('z', length)));
                    Check.InRange(result.Mask, 0, 7, "mascara para longitud " + length);
                }
            });

            runner.Test("la longitud declarada coincide con la real", delegate
            {
                string texto = "http://192.168.1.8:8080/";
                QrDecoder.Result result = QrDecoder.Decode(QrCode.Encode(texto));
                Check.Equal(Encoding.UTF8.GetByteCount(texto), result.DeclaredLength, "contador de caracteres");
            });
        }

        /// <summary>Numero de caracteres que fuerza exactamente esa version.</summary>
        private static int VersionPayload(int version)
        {
            int[] maximos = { 0, 14, 26, 42, 62, 84, 106 };
            return maximos[version];
        }
    }
}
