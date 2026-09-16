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

            // --- Identity ---
            UaParserTests.Register(runner);
            DnsWireTests.Register(runner);
            VerdictEngineTests.Register(runner);
            ClientRegistryTests.Register(runner);
            OuiAndProbeTests.Register(runner);

            // --- Net ---
            PortAvailabilityTests.Register(runner);

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
