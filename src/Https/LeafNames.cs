using System;
using System.Collections.Generic;
using System.Net;

namespace LanShare.Https
{
    /// <summary>
    /// Los nombres que cubre un certificado de servidor: dominios y
    /// direcciones IP. Se guardan normalizados (dominios en minusculas,
    /// direcciones en su forma canonica) y ordenados, para que comparar dos
    /// conjuntos no dependa del orden en que se escribieron.
    /// </summary>
    public sealed class LeafNames
    {
        private readonly List<string> _dnsNames = new List<string>();
        private readonly List<string> _addresses = new List<string>();

        public LeafNames(IEnumerable<string> dnsNames, IEnumerable<IPAddress> addresses)
        {
            if (dnsNames != null)
            {
                foreach (string name in dnsNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    string normal = name.TrimEnd('.').ToLowerInvariant();
                    if (normal.Length > 0 && !_dnsNames.Contains(normal)) _dnsNames.Add(normal);
                }
            }

            if (addresses != null)
            {
                foreach (IPAddress address in addresses)
                {
                    if (address == null) continue;
                    string normal = address.ToString();
                    if (!_addresses.Contains(normal)) _addresses.Add(normal);
                }
            }

            _dnsNames.Sort(StringComparer.Ordinal);
            _addresses.Sort(StringComparer.Ordinal);
        }

        public IList<string> DnsNames
        {
            get { return _dnsNames.AsReadOnly(); }
        }

        /// <summary>Direcciones en su forma de texto canonica ("192.168.1.5", "::1").</summary>
        public IList<string> Addresses
        {
            get { return _addresses.AsReadOnly(); }
        }

        /// <summary>Si este conjunto incluye todos los nombres de <paramref name="other"/>.</summary>
        public bool Covers(LeafNames other)
        {
            if (other == null) return true;

            foreach (string name in other._dnsNames)
            {
                if (_dnsNames.BinarySearch(name, StringComparer.Ordinal) < 0) return false;
            }
            foreach (string address in other._addresses)
            {
                if (_addresses.BinarySearch(address, StringComparer.Ordinal) < 0) return false;
            }
            return true;
        }

        /// <summary>Lista legible para el registro: "localhost, 127.0.0.1, 192.168.1.5".</summary>
        public string Describe()
        {
            List<string> all = new List<string>(_dnsNames);
            all.AddRange(_addresses);
            return string.Join(", ", all.ToArray());
        }
    }
}
