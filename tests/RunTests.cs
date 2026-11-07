using System;

namespace LanShare.Tests
{
    /// <summary>
    /// Punto de entrada de la bateria de pruebas. Devuelve cero si todo pasa,
    /// para poder encadenarlo con la compilacion.
    /// </summary>
    internal static class TestProgram
    {
        private static int Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("===========================================================");
            Console.WriteLine("  LanShare - bateria de pruebas");
            Console.WriteLine("===========================================================");
            Console.WriteLine();

            TestRunner runner = new TestRunner();

            // --- Core ---
            RangeParserTests.Register(runner);
            MimeRegistryTests.Register(runner);
            PathGuardTests.Register(runner);
            StaticFileResolverTests.Register(runner);
            EntityTagTests.Register(runner);
            CompressionTests.Register(runner);
            DirectoryListingTests.Register(runner);
            InfrastructureTests.Register(runner);
            SiteTests.Register(runner);
            QrCodeTests.Register(runner);
            MultipartReaderTests.Register(runner);
            UploadTests.Register(runner);
            LiveReloadTests.Register(runner);
            SettingsPersistenceTests.Register(runner);
            ServerExtrasTests.Register(runner);
            RemoteConsoleTests.Register(runner);
            DirectoryNavigationTests.Register(runner);
            ZipTests.Register(runner);
            DroppedPathTests.Register(runner);
            CertificatePageTests.Register(runner);
            HttpsSiteTests.Register(runner);

            // --- Https ---
            DerTests.Register(runner);
            NameConstraintsTests.Register(runner);
            LeafPolicyTests.Register(runner);
            CertificateExtensionTests.Register(runner);
            CertificateFactoryTests.Register(runner);
            HttpSysBindingTests.Register(runner);
            ServerCleanupTests.Register(runner);

            // --- Identity ---
            UaParserTests.Register(runner);
            DnsWireTests.Register(runner);
            VerdictEngineTests.Register(runner);
            ClientRegistryTests.Register(runner);
            OuiAndProbeTests.Register(runner);

            // --- Net ---
            PortAvailabilityTests.Register(runner);
            LanAddressRankingTests.Register(runner);

            // --- Ui ---
            PaneHeightsTests.Register(runner);

            // --- Arranque y plataforma ---
            PlatformTests.Register(runner);

            int exitCode = runner.Report();

            if (args != null && Array.IndexOf(args, "--pause") >= 0)
            {
                Console.WriteLine("Pulsa una tecla para salir...");
                Console.ReadKey(true);
            }

            return exitCode;
        }
    }
}
