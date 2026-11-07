using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace LanShare.Https
{
    /// <summary>Por que hay que emitir un certificado de servidor nuevo, o que no hace falta.</summary>
    public enum LeafRenewal
    {
        NotNeeded,
        Missing,
        WrongAuthority,
        Expiring,
        MissingNames
    }

    /// <summary>
    /// Reglas del certificado del servidor: que nombres cubre y cuando hay
    /// que sustituirlo. Funciones puras, sin almacen de certificados.
    /// </summary>
    public static class LeafPolicy
    {
        /// <summary>
        /// Vigencia. iOS y macOS rechazan certificados de servidor de mas de
        /// 825 dias aunque los firme una autoridad instalada a mano; 397 deja
        /// margen incluso frente al limite de 398 de los certificados publicos.
        /// Al caducar se renueva solo, con la misma autoridad: el movil no nota nada.
        /// </summary>
        public const int ValidityDays = 397;

        /// <summary>Se renueva con este margen, para no apurar hasta el ultimo dia.</summary>
        public const int RenewBeforeDays = 30;

        /// <summary>Margen hacia atras en el inicio de validez, por si el reloj del movil va retrasado.</summary>
        public const int BackdateDays = 1;

        private const int MaxDnsLabelLength = 63;

        /// <summary>
        /// Lo que debe cubrir el certificado: localhost y su bucle local, el
        /// nombre .local del equipo (el que anuncia Windows por mDNS) y las
        /// direcciones IPv4 de la red local. Las que la autoridad no podria
        /// avalar (publicas, 100.64/10) se dejan fuera: con ellas, ningun
        /// navegador aceptaria el certificado entero.
        /// </summary>
        public static LeafNames Desired(string machineName, IEnumerable<string> lanAddresses)
        {
            List<string> dnsNames = new List<string>();
            dnsNames.Add("localhost");
            if (IsValidDnsLabel(machineName)) dnsNames.Add(machineName.ToLowerInvariant() + ".local");

            List<IPAddress> addresses = new List<IPAddress>();
            addresses.Add(IPAddress.Loopback);
            addresses.Add(IPAddress.IPv6Loopback);

            if (lanAddresses != null)
            {
                foreach (string text in lanAddresses)
                {
                    IPAddress address;
                    if (!IPAddress.TryParse(text, out address)) continue;
                    if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (!LocalNameConstraints.PermitsAddress(address)) continue;
                    addresses.Add(address);
                }
            }

            return new LeafNames(dnsNames, addresses);
        }

        /// <summary>
        /// Solo el bucle local (localhost, 127.0.0.1, ::1): lo que necesitan los
        /// sitios que se sirven unicamente en este equipo.
        /// </summary>
        public static LeafNames LoopbackOnly()
        {
            return Desired(null, null);
        }

        /// <summary>
        /// Si hay que sustituir ya la autoridad. Cuando le queda menos de lo que
        /// dura un certificado de servidor mas su margen de renovacion, los que
        /// firmase saldrian recortados a su caducidad y habria que renovarlos
        /// en cada arranque hasta el final. Con diez anos de vida, ocurre
        /// cuando ha cumplido casi nueve.
        /// </summary>
        public static bool AuthorityNeedsReplacement(DateTime authorityNotAfterUtc, DateTime nowUtc)
        {
            return authorityNotAfterUtc - nowUtc < TimeSpan.FromDays(ValidityDays + RenewBeforeDays);
        }

        /// <summary>
        /// Una etiqueta DNS valida: de 1 a 63 caracteres, letras ASCII, cifras
        /// y guiones, sin guion al principio ni al final. Un nombre de equipo
        /// con otros caracteres no se puede escribir en el certificado.
        /// </summary>
        public static bool IsValidDnsLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || label.Length > MaxDnsLabelLength) return false;
            if (label[0] == '-' || label[label.Length - 1] == '-') return false;

            foreach (char c in label)
            {
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool digit = c >= '0' && c <= '9';
                if (!letter && !digit && c != '-') return false;
            }
            return true;
        }

        /// <summary>Decide si el certificado actual sigue valiendo.</summary>
        /// <param name="exists">Si hay alguno instalado.</param>
        /// <param name="issuedByCurrentAuthority">Si lo firmo la autoridad que existe ahora.</param>
        /// <param name="notAfterUtc">Fin de su validez.</param>
        /// <param name="covered">Nombres que cubre.</param>
        /// <param name="desired">Nombres que deberia cubrir hoy.</param>
        public static LeafRenewal Evaluate(bool exists, bool issuedByCurrentAuthority, DateTime notAfterUtc,
                                           DateTime nowUtc, LeafNames covered, LeafNames desired)
        {
            if (!exists) return LeafRenewal.Missing;
            if (!issuedByCurrentAuthority) return LeafRenewal.WrongAuthority;
            if (notAfterUtc - nowUtc < TimeSpan.FromDays(RenewBeforeDays)) return LeafRenewal.Expiring;
            if (covered == null || !covered.Covers(desired)) return LeafRenewal.MissingNames;
            return LeafRenewal.NotNeeded;
        }

        /// <summary>Motivo legible, para el registro.</summary>
        public static string Describe(LeafRenewal renewal)
        {
            switch (renewal)
            {
                case LeafRenewal.Missing: return "aun no habia certificado de servidor";
                case LeafRenewal.WrongAuthority: return "el certificado lo firmo una autoridad anterior";
                case LeafRenewal.Expiring: return "el certificado caduca en menos de " + RenewBeforeDays + " dias";
                case LeafRenewal.MissingNames: return "el equipo tiene direcciones que el certificado no cubre";
                default: return "el certificado sigue siendo valido";
            }
        }
    }
}
