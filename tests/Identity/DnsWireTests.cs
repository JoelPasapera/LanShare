using System;
using System.Collections.Generic;
using System.Text;
using LanShare.Identity;

namespace LanShare.Tests
{
    /// <summary>
    /// Serializacion de nombres DNS, compartida por mDNS y NetBIOS. Un fallo en
    /// la compresion por punteros descuadra todo lo que venga detras.
    /// </summary>
    public static class DnsWireTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Identity / DnsWire");

            runner.Test("un nombre se escribe con etiquetas y terminador", delegate
            {
                List<byte> buffer = new List<byte>();
                DnsWire.WriteName(buffer, "abc.de");

                byte[] esperado = { 3, (byte)'a', (byte)'b', (byte)'c', 2, (byte)'d', (byte)'e', 0 };
                Check.BytesEqual(esperado, buffer.ToArray(), "codificacion");
            });

            runner.Test("ida y vuelta de un nombre sencillo", delegate
            {
                List<byte> buffer = new List<byte>();
                DnsWire.WriteName(buffer, "equipo.local");

                int next;
                Check.Equal("equipo.local", DnsWire.ReadName(buffer.ToArray(), 0, out next), "nombre");
                Check.Equal(buffer.Count, next, "posicion siguiente");
            });

            runner.Test("ida y vuelta de un nombre largo", delegate
            {
                List<byte> buffer = new List<byte>();
                DnsWire.WriteName(buffer, "8.1.168.192.in-addr.arpa");

                int next;
                Check.Equal("8.1.168.192.in-addr.arpa", DnsWire.ReadName(buffer.ToArray(), 0, out next),
                            "nombre inverso");
            });

            runner.Test("las etiquetas vacias se descartan", delegate
            {
                List<byte> buffer = new List<byte>();
                DnsWire.WriteName(buffer, "a..b");

                int next;
                Check.Equal("a.b", DnsWire.ReadName(buffer.ToArray(), 0, out next), "sin etiqueta vacia");
            });

            runner.Test("se sigue un puntero de compresion", delegate
            {
                // "local" en el desplazamiento 0, y en el 7 un nombre "mi" que
                // apunta al anterior: deberia leerse "mi.local".
                List<byte> datos = new List<byte>();
                DnsWire.WriteName(datos, "local");          // 0..6
                datos.Add(2); datos.Add((byte)'m'); datos.Add((byte)'i');
                datos.Add(0xC0); datos.Add(0x00);

                int next;
                Check.Equal("mi.local", DnsWire.ReadName(datos.ToArray(), 7, out next), "nombre compuesto");
                Check.Equal(12, next, "la posicion siguiente es la del puntero, no la del destino");
            });

            runner.Test("SEGURIDAD: un puntero circular no cuelga el lector", delegate
            {
                // Un paquete malicioso puede encadenar punteros que se apuntan
                // entre si. Sin tope de saltos esto seria un bucle infinito.
                byte[] datos = { 0xC0, 0x02, 0xC0, 0x00 };

                int next;
                Check.DoesNotThrow(delegate { DnsWire.ReadName(datos, 0, out next); }, "no lanza");
            });

            runner.Test("SEGURIDAD: una longitud que se sale del buffer no revienta", delegate
            {
                byte[] datos = { 50, (byte)'a', (byte)'b' };   // dice 50 y solo hay 2
                int next;
                Check.DoesNotThrow(delegate { DnsWire.ReadName(datos, 0, out next); }, "no lanza");
            });

            runner.Test("SkipName deja la posicion tras el terminador", delegate
            {
                List<byte> datos = new List<byte>();
                DnsWire.WriteName(datos, "abc");
                datos.Add(0xAA);   // marca posterior

                Check.Equal(datos.Count - 1, DnsWire.SkipName(datos.ToArray(), 0), "posicion siguiente");
            });

            runner.Test("un nombre vacio se lee sin problema", delegate
            {
                byte[] datos = { 0, 0xAA };
                int next;
                Check.Equal("", DnsWire.ReadName(datos, 0, out next), "nombre vacio");
                Check.Equal(1, next, "posicion siguiente");
            });

            runner.Test("se quita el sufijo .local", delegate
            {
                Check.Equal("mi-equipo", DnsWire.StripLocalSuffix("mi-equipo.local"), "con sufijo");
                Check.Equal("mi-equipo", DnsWire.StripLocalSuffix("mi-equipo.LOCAL"), "en mayusculas");
                Check.Equal("mi-equipo", DnsWire.StripLocalSuffix("mi-equipo"), "sin sufijo");
                Check.Null(DnsWire.StripLocalSuffix(null), "nulo");
            });
        }
    }
}
