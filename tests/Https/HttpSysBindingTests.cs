using System;
using System.Collections.Generic;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// La parte de http.sys que se puede probar sin administrador y sin
    /// cambiar nada: el tamano de las estructuras que se le pasan y la
    /// lectura de las asociaciones que ya existan en este equipo.
    /// </summary>
    public static class HttpSysBindingTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / http.sys (solo lectura)");

            runner.Test("CLAVE: las estructuras miden lo mismo que en http.h", delegate
            {
                // Un desajuste aqui da ERROR_INVALID_PARAMETER al asociar, sin mas pista.
                bool is64 = IntPtr.Size == 8;
                Check.Equal(is64 ? 88 : 56, HttpSysCertBinding.SslSetSize, "HTTP_SERVICE_CONFIG_SSL_SET");
                Check.Equal(is64 ? 24 : 12, HttpSysCertBinding.SslQuerySize, "HTTP_SERVICE_CONFIG_SSL_QUERY");
            });

            runner.Test("leer todas las asociaciones no necesita administrador ni falla", delegate
            {
                // En un equipo con IIS Express o Visual Studio suele haber
                // varias: leerlas comprueba la disposicion de las estructuras
                // contra datos reales de Windows.
                List<SslBinding> all = HttpSysCertBinding.ListAll();
                foreach (SslBinding binding in all)
                {
                    Check.InRange(binding.Port, 1, 65535, "puerto de una asociacion existente");
                    Check.InRange(binding.CertificateHash.Length, 1, 64, "huella de una asociacion existente");
                }
            });

            runner.Test("los puertos propios salen de la misma lectura", delegate
            {
                List<int> ours = HttpSysCertBinding.FindOurPorts();
                foreach (int port in ours) Check.InRange(port, 1, 65535, "puerto propio");
            });

            runner.Test("un puerto sin asociar no cuenta como asociado", delegate
            {
                // El puerto 1 (tcpmux) no lo usa nadie para HTTPS.
                Check.False(HttpSysCertBinding.IsBound(1, new byte[20]), "puerto 1");
            });

            runner.Test("el identificador de LanShare no cambia nunca", delegate
            {
                // Con otro, las asociaciones de versiones anteriores quedarian
                // huerfanas: ni se reconocerian ni se podrian limpiar.
                Check.Equal(new Guid("3f6c9a1e-52b7-4d08-9e4a-7c1d2b8f6a35"), HttpSysCertBinding.AppId, "AppId");
            });
        }
    }
}
