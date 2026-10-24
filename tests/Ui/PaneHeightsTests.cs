using System;
using LanShare.Ui;

namespace LanShare.Tests
{
    /// <summary>
    /// Reparto de altura entre las cajas de la ventana. Los numeros se parecen
    /// a los de la ventana principal (sitios 174, estado 122 y pestanas 329)
    /// pero son de la prueba: lo que se comprueba es el algoritmo.
    /// </summary>
    public static class PaneHeightsTests
    {
        private static readonly int[] Minimos = { 112, 110, 260 };

        private static int[] Fabrica()
        {
            return new int[] { 174, 122, 329 };
        }

        /// <summary>Compara como texto: si falla, el mensaje ensena las dos listas enteras.</summary>
        private static void Alturas(int[] esperadas, int[] reales, string what)
        {
            Check.Equal(Lista(esperadas), Lista(reales), what);
        }

        private static string Lista(int[] valores)
        {
            string[] partes = new string[valores.Length];
            for (int i = 0; i < valores.Length; i++) partes[i] = valores[i].ToString();
            return string.Join(",", partes);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Ui / PaneHeights - mover un limite");

            runner.Test("bajar el borde inferior del estado lo agranda y encoge las pestanas", delegate
            {
                int[] h = Fabrica();
                Check.Equal(50, PaneHeights.MoveBoundary(h, Minimos, 1, 50), "aplicado");
                Alturas(new int[] { 174, 172, 279 }, h, "alturas");
            });

            runner.Test("las pestanas no bajan de su minimo: el limite se para", delegate
            {
                int[] h = Fabrica();
                Check.Equal(69, PaneHeights.MoveBoundary(h, Minimos, 1, 500), "aplicado");
                Alturas(new int[] { 174, 191, 260 }, h, "alturas");
            });

            runner.Test("subir el borde superior de las pestanas encoge el estado y luego empuja a los sitios", delegate
            {
                int[] h = Fabrica();
                Check.Equal(-40, PaneHeights.MoveBoundary(h, Minimos, 1, -40), "aplicado");
                Alturas(new int[] { 146, 110, 369 }, h, "alturas");
            });

            runner.Test("bajar el borde de los sitios encoge el estado y despues las pestanas", delegate
            {
                int[] h = Fabrica();
                Check.Equal(80, PaneHeights.MoveBoundary(h, Minimos, 0, 80), "aplicado");
                Alturas(new int[] { 254, 110, 261 }, h, "alturas");
            });

            runner.Test("subir el borde de los sitios agranda el estado y deja las pestanas", delegate
            {
                int[] h = Fabrica();
                Check.Equal(-30, PaneHeights.MoveBoundary(h, Minimos, 0, -30), "aplicado");
                Alturas(new int[] { 144, 152, 329 }, h, "alturas");
            });

            runner.Test("con todo en su minimo no se mueve nada", delegate
            {
                int[] h = { 112, 110, 260 };
                Check.Equal(0, PaneHeights.MoveBoundary(h, Minimos, 1, -50), "hacia arriba");
                Check.Equal(0, PaneHeights.MoveBoundary(h, Minimos, 0, 50), "hacia abajo");
                Alturas(new int[] { 112, 110, 260 }, h, "intactas");
            });

            runner.Test("dos cajas: lista de clientes y detalle", delegate
            {
                int[] h = { 170, 200 };
                Check.Equal(112, PaneHeights.MoveBoundary(h, new int[] { 90, 88 }, 0, 150), "hasta el minimo del detalle");
                Alturas(new int[] { 282, 88 }, h, "alturas");
            });

            runner.Test("INVARIANTE: la suma se conserva y nadie baja de su minimo", delegate
            {
                Random random = new Random(20261023);
                for (int vuelta = 0; vuelta < 5000; vuelta++)
                {
                    int cajas = random.Next(2, 6);
                    int[] h = new int[cajas];
                    int[] m = new int[cajas];
                    int suma = 0;
                    for (int i = 0; i < cajas; i++)
                    {
                        m[i] = random.Next(0, 200);
                        h[i] = m[i] + random.Next(0, 300);
                        suma += h[i];
                    }

                    int limite = random.Next(0, cajas - 1);
                    int pedido = random.Next(-800, 801);
                    int aplicado = PaneHeights.MoveBoundary(h, m, limite, pedido);

                    int nueva = 0;
                    for (int i = 0; i < cajas; i++)
                    {
                        nueva += h[i];
                        if (h[i] < m[i]) Check.True(false, "caja " + i + " bajo su minimo en la vuelta " + vuelta);
                    }
                    Check.Equal(suma, nueva, "suma en la vuelta " + vuelta);
                    Check.True(Math.Abs(aplicado) <= Math.Abs(pedido), "no se pasa de lo pedido");
                    Check.True(aplicado == 0 || Math.Sign(aplicado) == Math.Sign(pedido), "mismo sentido");
                }
            });

            runner.Test("datos mal formados lanzan", delegate
            {
                Check.Throws(delegate { PaneHeights.MoveBoundary(null, Minimos, 0, 1); }, "alturas nulas");
                Check.Throws(delegate { PaneHeights.MoveBoundary(Fabrica(), new int[] { 1, 2 }, 0, 1); }, "minimos de otra longitud");
                Check.Throws(delegate { PaneHeights.MoveBoundary(Fabrica(), Minimos, 2, 1); }, "no hay limite bajo la ultima");
                Check.Throws(delegate { PaneHeights.MoveBoundary(new int[] { 10 }, new int[] { 1 }, 0, 1); }, "una sola caja");
            });

            runner.Suite("Ui / PaneHeights - encajar en la ventana");

            runner.Test("al agrandar la ventana, todo lo nuevo es para las pestanas", delegate
            {
                Alturas(new int[] { 174, 122, 429 }, PaneHeights.Fit(Fabrica(), Minimos, 725), "alturas");
            });

            runner.Test("al achicarla encoge primero el estado y despues los sitios", delegate
            {
                Alturas(new int[] { 130, 110, 260 }, PaneHeights.Fit(Fabrica(), Minimos, 500), "alturas");
            });

            runner.Test("si ni asi cabe, las pestanas se quedan por debajo de su minimo", delegate
            {
                Alturas(new int[] { 112, 110, 178 }, PaneHeights.Fit(Fabrica(), Minimos, 400), "alturas");
            });

            runner.Test("una altura guardada por debajo del minimo se corrige", delegate
            {
                Alturas(new int[] { 112, 122, 391 }, PaneHeights.Fit(new int[] { 50, 122, 0 }, Minimos, 625), "alturas");
            });

            runner.Test("encajar no toca las alturas elegidas: al volver el sitio, vuelven", delegate
            {
                int[] elegidas = { 300, 200, 0 };
                PaneHeights.Fit(elegidas, Minimos, 500);
                Alturas(new int[] { 300, 200, 0 }, elegidas, "sin modificar");
                Alturas(new int[] { 300, 200, 400 }, PaneHeights.Fit(elegidas, Minimos, 900), "con sitio de sobra");
            });
        }
    }
}
