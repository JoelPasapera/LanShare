using System;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// Codificador y lector DER escritos a mano. DER solo admite una forma de
    /// escribir cada valor, asi que basta comparar bytes; y el lector tiene
    /// que rechazar con FormatException cualquier cosa mal formada, nunca
    /// leer fuera del arreglo.
    /// </summary>
    public static class DerTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / DER - escritura");

            runner.Test("longitud corta hasta 127 y larga a partir de 128", delegate
            {
                Check.Equal("00", Hex.Format(DerWriter.EncodeLength(0)), "0");
                Check.Equal("7f", Hex.Format(DerWriter.EncodeLength(127)), "127");
                Check.Equal("8180", Hex.Format(DerWriter.EncodeLength(128)), "128");
                Check.Equal("81ff", Hex.Format(DerWriter.EncodeLength(255)), "255");
                Check.Equal("820100", Hex.Format(DerWriter.EncodeLength(256)), "256");
                Check.Equal("82ffff", Hex.Format(DerWriter.EncodeLength(65535)), "65535");
                Check.Equal("83010000", Hex.Format(DerWriter.EncodeLength(65536)), "65536");
                Check.Throws(delegate { DerWriter.EncodeLength(-1); }, "negativa");
            });

            runner.Test("etiqueta, longitud y valor", delegate
            {
                Check.Equal("0403010203", Hex.Format(DerWriter.OctetString(new byte[] { 1, 2, 3 })), "OCTET STRING");
                Check.Equal("3000", Hex.Format(DerWriter.Sequence()), "SEQUENCE vacia");
                Check.Equal("3005040100" + "0500", Hex.Format(DerWriter.Sequence(
                    DerWriter.OctetString(new byte[] { 0 }), new byte[] { 0x05, 0x00 })), "SEQUENCE con dos elementos");

                byte[] large = DerWriter.OctetString(new byte[200]);
                Check.Equal(203, large.Length, "200 bytes llevan dos de longitud");
                Check.Equal("0481c8", Hex.Format(new byte[] { large[0], large[1], large[2] }), "cabecera larga");
            });

            runner.Test("etiquetas de contexto primitivas y construidas", delegate
            {
                Check.Equal("820161", Hex.Format(DerWriter.ContextPrimitive(2, new byte[] { 0x61 })), "[2] primitiva");
                Check.Equal("870100", Hex.Format(DerWriter.ContextPrimitive(7, new byte[] { 0 })), "[7] primitiva");
                Check.Equal("a000", Hex.Format(DerWriter.ContextConstructed(0, new byte[0])), "[0] construida");
                Check.Throws(delegate { DerWriter.ContextPrimitive(31, new byte[0]); }, "numero de etiqueta largo");
                Check.Throws(delegate { DerWriter.ContextPrimitive(-1, new byte[0]); }, "numero negativo");
            });

            runner.Test("partes nulas se rechazan", delegate
            {
                Check.Throws(delegate { DerWriter.Encode(0x04, null); }, "contenido nulo");
                Check.Throws(delegate { DerWriter.Concat(new byte[] { 1 }, null); }, "una parte nula");
            });

            runner.Suite("Https / DER - lectura");

            runner.Test("lee de vuelta lo que escribe el codificador", delegate
            {
                byte[] encoded = DerWriter.Sequence(
                    DerWriter.OctetString(new byte[] { 1, 2, 3 }),
                    DerWriter.ContextPrimitive(7, new byte[] { 9, 9 }));

                DerReader reader = new DerReader(encoded);
                DerElement sequence = reader.Read(DerWriter.TagSequence);
                Check.False(reader.HasMore, "un solo elemento en la raiz");

                DerReader children = sequence.Children();
                DerElement first = children.Read(DerWriter.TagOctetString);
                Check.Equal("010203", Hex.Format(first.Content()), "primer hijo");

                DerElement second = children.Read();
                Check.Equal((byte)0x87, second.Tag, "etiqueta del segundo hijo");
                Check.Equal("0909", Hex.Format(second.Content()), "segundo hijo");
                Check.False(children.HasMore, "no hay mas hijos");
            });

            runner.Test("longitudes largas se leen igual", delegate
            {
                byte[] content = new byte[300];
                for (int i = 0; i < content.Length; i++) content[i] = (byte)i;

                DerElement element = new DerReader(DerWriter.OctetString(content)).Read(DerWriter.TagOctetString);
                Check.Equal(300, element.ContentLength, "longitud");
                Check.BytesEqual(content, element.Content(), "contenido");
            });

            runner.Test("lo mal formado da FormatException", delegate
            {
                ExpectFormatError(new byte[0], "vacio");
                ExpectFormatError(new byte[] { 0x30 }, "sin longitud");
                ExpectFormatError(new byte[] { 0x30, 0x05, 0x01 }, "longitud mayor que lo que queda");
                ExpectFormatError(new byte[] { 0x30, 0x80, 0x00, 0x00 }, "longitud indefinida de BER");
                ExpectFormatError(new byte[] { 0x30, 0x85, 0, 0, 0, 0, 1 }, "cinco bytes de longitud");
                ExpectFormatError(new byte[] { 0x30, 0x82, 0x01 }, "longitud cortada");
                ExpectFormatError(new byte[] { 0x1F, 0x01, 0x00 }, "etiqueta de varios bytes");
                ExpectFormatError(new byte[] { 0x30, 0x84, 0x7F, 0xFF, 0xFF, 0xFF }, "longitud enorme");
            });

            runner.Test("una etiqueta inesperada da FormatException", delegate
            {
                byte[] octets = DerWriter.OctetString(new byte[] { 1 });
                ExpectFormat(delegate { new DerReader(octets).Read(DerWriter.TagSequence); }, "OCTET STRING por SEQUENCE");
            });

            runner.Test("un hijo no puede salirse de su padre", delegate
            {
                // SEQUENCE de 3 bytes cuyo hijo dice medir 5: los bytes estan
                // en el arreglo, pero fuera del padre.
                byte[] data = { 0x30, 0x03, 0x04, 0x05, 0x01, 0x02, 0x03, 0x04, 0x05 };
                DerElement parent = new DerReader(data, 0, 5).Read(DerWriter.TagSequence);
                ExpectFormat(delegate { parent.Children().Read(); }, "hijo mas largo que el padre");
            });

            runner.Test("los limites del lector se comprueban", delegate
            {
                Check.Throws(delegate { new DerReader(null); }, "datos nulos");
                Check.Throws(delegate { new DerReader(new byte[4], 2, 3); }, "ventana fuera del arreglo");
                Check.Throws(delegate { new DerReader(new byte[4], -1, 1); }, "inicio negativo");
            });

            runner.Test("CLAVE: ningun byte cambiado ni corte rompe el lector", delegate
            {
                // Se parte de una estructura real (las restricciones de nombre)
                // y se estropea de todas las formas sencillas: cortandola por
                // cada posicion y cambiando cada byte por valores frontera.
                byte[] valid = LocalNameConstraints.Encode();
                byte[] replacements = { 0x00, 0x01, 0x7F, 0x80, 0x81, 0x84, 0xFF };

                for (int length = 0; length < valid.Length; length++)
                {
                    byte[] cut = new byte[length];
                    Buffer.BlockCopy(valid, 0, cut, 0, length);
                    WalkOrFormatError(cut, "cortado a " + length);
                }

                for (int position = 0; position < valid.Length; position++)
                {
                    foreach (byte value in replacements)
                    {
                        byte[] changed = (byte[])valid.Clone();
                        changed[position] = value;
                        WalkOrFormatError(changed, "byte " + position + " cambiado por " + value);
                    }
                }
            });
        }

        /// <summary>Exige al menos un elemento y recorre el arbol entero.</summary>
        private static void ExpectFormatError(byte[] data, string what)
        {
            ExpectFormat(delegate { WalkRoot(data); }, what);
        }

        private static void ExpectFormat(Action action, string what)
        {
            try
            {
                action();
            }
            catch (FormatException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionException(what + ": se esperaba FormatException y llego " + ex.GetType().Name);
            }
            throw new AssertionException(what + ": se esperaba FormatException y no hubo ninguna");
        }

        /// <summary>Unos bytes cualesquiera: o se leen enteros o dan FormatException, nada mas.</summary>
        private static void WalkOrFormatError(byte[] data, string what)
        {
            try
            {
                WalkRoot(data);
            }
            catch (FormatException)
            {
            }
            catch (Exception ex)
            {
                throw new AssertionException(what + ": el lector lanzo " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void WalkRoot(byte[] data)
        {
            DerReader reader = new DerReader(data);
            Visit(reader.Read());
            Walk(reader);
        }

        private static void Walk(DerReader reader)
        {
            while (reader.HasMore) Visit(reader.Read());
        }

        /// <summary>Copia el valor y, si el elemento es construido (bit 0x20), entra en el.</summary>
        private static void Visit(DerElement element)
        {
            element.Content();
            if ((element.Tag & 0x20) != 0) Walk(element.Children());
        }
    }
}
