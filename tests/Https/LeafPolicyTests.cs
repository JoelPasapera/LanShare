using System;
using System.Collections.Generic;
using System.Net;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// Que nombres cubre el certificado del servidor y cuando se renueva.
    /// Funciones puras: ni almacenes ni claves.
    /// </summary>
    public static class LeafPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        private static LeafNames Names(string[] dnsNames, params string[] addresses)
        {
            List<IPAddress> parsed = new List<IPAddress>();
            foreach (string text in addresses) parsed.Add(IPAddress.Parse(text));
            return new LeafNames(dnsNames, parsed);
        }

        private static string Join(IList<string> values)
        {
            string[] copy = new string[values.Count];
            values.CopyTo(copy, 0);
            return string.Join(",", copy);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / nombres del certificado");

            runner.Test("normaliza, quita repetidos y ordena", delegate
            {
                LeafNames names = new LeafNames(
                    new string[] { "LOCALHOST.", "localhost", "", null, "Joel-PC.local" },
                    new IPAddress[] { IPAddress.Parse("192.168.1.5"), IPAddress.Loopback, null,
                                      IPAddress.Parse("127.0.0.1"), IPAddress.IPv6Loopback });

                Check.Equal("joel-pc.local,localhost", Join(names.DnsNames), "dominios");
                Check.Equal("127.0.0.1,192.168.1.5,::1", Join(names.Addresses), "direcciones");
            });

            runner.Test("listas nulas dan un conjunto vacio", delegate
            {
                LeafNames empty = new LeafNames(null, null);
                Check.Equal(0, empty.DnsNames.Count, "sin dominios");
                Check.Equal(0, empty.Addresses.Count, "sin direcciones");
                Check.True(empty.Covers(empty), "vacio cubre vacio");
            });

            runner.Test("cubrir es incluir todos los nombres del otro", delegate
            {
                LeafNames big = Names(new string[] { "localhost", "pc.local" }, "127.0.0.1", "192.168.1.5", "::1");
                LeafNames small = Names(new string[] { "localhost" }, "192.168.1.5");
                LeafNames other = Names(new string[] { "localhost" }, "192.168.1.6");

                Check.True(big.Covers(small), "superconjunto");
                Check.False(small.Covers(big), "subconjunto");
                Check.False(big.Covers(other), "una direccion distinta");
                Check.True(big.Covers(null), "nada que cubrir");
                Check.True(big.Covers(big), "a si mismo");
            });

            runner.Test("la descripcion pone dominios y luego direcciones", delegate
            {
                LeafNames names = Names(new string[] { "localhost" }, "127.0.0.1", "192.168.1.5");
                Check.Equal("localhost, 127.0.0.1, 192.168.1.5", names.Describe(), "descripcion");
            });

            runner.Suite("Https / reglas del certificado del servidor");

            runner.Test("cubre localhost, el nombre .local y las IPv4 privadas", delegate
            {
                LeafNames desired = LeafPolicy.Desired("JOEL-PC", new string[] { "192.168.1.5", "10.0.0.7" });
                Check.Equal("joel-pc.local,localhost", Join(desired.DnsNames), "dominios");
                Check.Equal("10.0.0.7,127.0.0.1,192.168.1.5,::1", Join(desired.Addresses), "direcciones");
            });

            runner.Test("CLAVE: deja fuera lo que la autoridad no podria avalar", delegate
            {
                // Con una sola direccion no permitida, el navegador rechazaria
                // el certificado entero: se omite y el resto sigue valiendo.
                LeafNames desired = LeafPolicy.Desired("PC", new string[]
                {
                    "8.8.8.8", "100.64.1.2", "fe80::1", "no es una ip", null, "", "172.20.0.4"
                });
                Check.Equal("127.0.0.1,172.20.0.4,::1", Join(desired.Addresses), "direcciones");
            });

            runner.Test("los sitios solo de este equipo solo necesitan el bucle local", delegate
            {
                LeafNames loopback = LeafPolicy.LoopbackOnly();
                Check.Equal("localhost", Join(loopback.DnsNames), "dominios");
                Check.Equal("127.0.0.1,::1", Join(loopback.Addresses), "direcciones");

                // Un certificado emitido para la red local sigue valiendo para
                // ellos: pasar a servir solo en el equipo no obliga a renovar.
                LeafNames lan = LeafPolicy.Desired("PC", new string[] { "192.168.1.5" });
                Check.Equal(LeafRenewal.NotNeeded,
                            LeafPolicy.Evaluate(true, true, Now.AddDays(100), Now, lan, loopback), "red local cubre bucle");
            });

            runner.Test("un nombre de equipo no valido como DNS no entra", delegate
            {
                Check.Equal("localhost", Join(LeafPolicy.Desired("PC_01", null).DnsNames), "guion bajo");
                Check.Equal("localhost", Join(LeafPolicy.Desired("", null).DnsNames), "vacio");
                Check.Equal("localhost", Join(LeafPolicy.Desired(null, null).DnsNames), "nulo");
                Check.Equal("127.0.0.1,::1", Join(LeafPolicy.Desired(null, null).Addresses), "solo el bucle local");
            });

            runner.Test("etiquetas DNS validas e invalidas", delegate
            {
                Check.True(LeafPolicy.IsValidDnsLabel("PC"), "corta");
                Check.True(LeafPolicy.IsValidDnsLabel("joel-pc-2"), "con guiones");
                Check.True(LeafPolicy.IsValidDnsLabel("a"), "una letra");
                Check.True(LeafPolicy.IsValidDnsLabel(new string('a', 63)), "63 caracteres");

                Check.False(LeafPolicy.IsValidDnsLabel(new string('a', 64)), "64 caracteres");
                Check.False(LeafPolicy.IsValidDnsLabel("-pc"), "guion inicial");
                Check.False(LeafPolicy.IsValidDnsLabel("pc-"), "guion final");
                Check.False(LeafPolicy.IsValidDnsLabel("pc_1"), "guion bajo");
                Check.False(LeafPolicy.IsValidDnsLabel("pc.local"), "con punto");
                Check.False(LeafPolicy.IsValidDnsLabel("pc 1"), "con espacio");
                Check.False(LeafPolicy.IsValidDnsLabel("equipo-\u00f1"), "no ASCII");
                Check.False(LeafPolicy.IsValidDnsLabel(""), "vacia");
                Check.False(LeafPolicy.IsValidDnsLabel(null), "nula");
            });

            runner.Test("los motivos de renovacion van en orden", delegate
            {
                LeafNames desired = LeafPolicy.Desired("PC", new string[] { "192.168.1.5" });
                LeafNames sameNames = LeafPolicy.Desired("PC", new string[] { "192.168.1.5" });
                DateTime farAway = Now.AddDays(200);

                Check.Equal(LeafRenewal.Missing,
                            LeafPolicy.Evaluate(false, false, DateTime.MinValue, Now, null, desired), "no hay");
                Check.Equal(LeafRenewal.WrongAuthority,
                            LeafPolicy.Evaluate(true, false, farAway, Now, sameNames, desired), "otra autoridad");
                Check.Equal(LeafRenewal.Expiring,
                            LeafPolicy.Evaluate(true, true, Now.AddDays(LeafPolicy.RenewBeforeDays - 1), Now,
                                                sameNames, desired), "caduca pronto");
                Check.Equal(LeafRenewal.MissingNames,
                            LeafPolicy.Evaluate(true, true, farAway, Now,
                                                LeafPolicy.Desired("PC", new string[] { "192.168.1.6" }), desired),
                            "direccion nueva");
                Check.Equal(LeafRenewal.MissingNames,
                            LeafPolicy.Evaluate(true, true, farAway, Now, null, desired), "sin nombres leidos");
                Check.Equal(LeafRenewal.NotNeeded,
                            LeafPolicy.Evaluate(true, true, farAway, Now, sameNames, desired), "sigue valiendo");
            });

            runner.Test("una direccion que desaparece no obliga a renovar", delegate
            {
                // Al quitar el cable sobra una direccion: el certificado sigue
                // cubriendo todo lo que hace falta.
                LeafNames covered = LeafPolicy.Desired("PC", new string[] { "192.168.1.5", "10.0.0.7" });
                LeafNames desired = LeafPolicy.Desired("PC", new string[] { "192.168.1.5" });
                Check.Equal(LeafRenewal.NotNeeded,
                            LeafPolicy.Evaluate(true, true, Now.AddDays(100), Now, covered, desired), "superconjunto");
            });

            runner.Test("el margen de renovacion es justo de " + LeafPolicy.RenewBeforeDays + " dias", delegate
            {
                LeafNames names = LeafPolicy.Desired("PC", null);
                DateTime edge = Now.AddDays(LeafPolicy.RenewBeforeDays);
                Check.Equal(LeafRenewal.NotNeeded, LeafPolicy.Evaluate(true, true, edge, Now, names, names), "en el borde");
                Check.Equal(LeafRenewal.Expiring,
                            LeafPolicy.Evaluate(true, true, edge.AddSeconds(-1), Now, names, names), "un segundo antes");
            });

            runner.Test("la autoridad se sustituye cuando ya no cabe un certificado entero", delegate
            {
                int margin = LeafPolicy.ValidityDays + LeafPolicy.RenewBeforeDays;
                Check.False(LeafPolicy.AuthorityNeedsReplacement(Now.AddYears(10), Now), "recien creada");
                Check.False(LeafPolicy.AuthorityNeedsReplacement(Now.AddDays(margin), Now), "justo en el margen");
                Check.True(LeafPolicy.AuthorityNeedsReplacement(Now.AddDays(margin).AddSeconds(-1), Now),
                           "un segundo dentro del margen");
                Check.True(LeafPolicy.AuthorityNeedsReplacement(Now.AddDays(-1), Now), "ya caducada");
            });

            runner.Test("la vigencia respeta los limites de Apple y de los navegadores", delegate
            {
                Check.True(LeafPolicy.ValidityDays + LeafPolicy.BackdateDays <= 398, "maximo de 398 dias");
                Check.True(LeafPolicy.RenewBeforeDays < LeafPolicy.ValidityDays, "se renueva antes de caducar");
            });

            runner.Test("cada motivo tiene su texto", delegate
            {
                List<string> seen = new List<string>();
                foreach (LeafRenewal renewal in Enum.GetValues(typeof(LeafRenewal)))
                {
                    string text = LeafPolicy.Describe(renewal);
                    Check.NotEmpty(text, "motivo " + renewal);
                    Check.False(seen.Contains(text), renewal + " repetido");
                    seen.Add(text);
                }
            });
        }
    }
}
