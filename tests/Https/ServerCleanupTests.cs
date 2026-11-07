using System;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// Que certificados de servidor sobran al renovar y que asociaciones de
    /// http.sys se pueden soltar. Si otro usuario de este equipo usa LanShare
    /// con HTTPS, lo suyo no se toca.
    /// </summary>
    public static class ServerCleanupTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Local);

        private static readonly byte[] Mine = { 1, 2, 3, 4 };
        private static readonly byte[] Theirs = { 9, 9, 9, 9 };

        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / limpieza de certificados del servidor");

            runner.Test("sobran los de esta autoridad, aunque sigan vigentes", delegate
            {
                Check.True(ServerCertificateStore.IsStale(Mine, Now.AddDays(200), Now, Mine), "vigente y mio");
                Check.True(ServerCertificateStore.IsStale(Mine, Now.AddDays(-1), Now, Mine), "caducado y mio");
            });

            runner.Test("CLAVE: los vigentes de otra autoridad son de otro usuario y se quedan", delegate
            {
                Check.False(ServerCertificateStore.IsStale(Theirs, Now.AddDays(200), Now, Mine), "de otro usuario");
                Check.False(ServerCertificateStore.IsStale(Theirs, Now.AddDays(200), Now, null),
                            "sin autoridad propia no se borra lo ajeno");
                Check.False(ServerCertificateStore.IsStale(null, Now.AddDays(200), Now, Mine),
                            "sin identificador de autoridad tampoco");
            });

            runner.Test("los caducados sobran sean de quien sean", delegate
            {
                Check.True(ServerCertificateStore.IsStale(Theirs, Now.AddDays(-1), Now, Mine), "caducado de otro");
                Check.True(ServerCertificateStore.IsStale(Theirs, Now, Now, null), "caduca justo ahora");
                Check.True(ServerCertificateStore.IsStale(null, Now.AddSeconds(-1), Now, null), "caducado sin AKI");
            });
        }
    }
}
