using System;

namespace LanShare.Ui
{
    /// <summary>
    /// Reparto de altura entre cajas apiladas y separadas por divisores, de
    /// arriba abajo; la ultima es la que rellena lo que sobra.
    ///
    /// Funcion pura, sin controles, para poder probarla: la usan los divisores
    /// de la ventana para mover los limites entre cajas y para encajarlas
    /// cuando la ventana cambia de tamano.
    /// </summary>
    public static class PaneHeights
    {
        /// <summary>
        /// Mueve el limite que hay debajo de la caja <paramref name="boundary"/>.
        /// Hacia abajo, esa caja crece y las de debajo encogen por orden, cada
        /// una hasta su minimo. Hacia arriba, encogen ella y las de encima, por
        /// orden, y crece la de justo debajo. Asi el limite sigue al raton hasta
        /// donde se pueda empujar. La suma de alturas no cambia.
        /// </summary>
        /// <param name="heights">Alturas actuales; se modifican en el sitio.</param>
        /// <param name="minimums">Altura minima de cada caja.</param>
        /// <param name="boundary">Caja que queda encima del limite: de 0 a n-2.</param>
        /// <param name="delta">Pixeles que se mueve el limite, positivo hacia abajo.</param>
        /// <returns>Desplazamiento aplicado, con signo; menor si se topa con los minimos.</returns>
        public static int MoveBoundary(int[] heights, int[] minimums, int boundary, int delta)
        {
            Validate(heights, minimums);
            if (boundary < 0 || boundary > heights.Length - 2) throw new ArgumentOutOfRangeException("boundary");
            if (delta == 0) return 0;

            if (delta > 0)
            {
                int lowered = Take(heights, minimums, boundary + 1, heights.Length - 1, delta);
                heights[boundary] += lowered;
                return lowered;
            }

            int raised = Take(heights, minimums, boundary, 0, -delta);
            heights[boundary + 1] += raised;
            return -raised;
        }

        /// <summary>
        /// Encaja las alturas deseadas en un total. La ultima caja se queda con
        /// lo que sobre o falte y, si con eso no llega a su minimo, encogen las
        /// de encima empezando por la mas cercana. Si ni asi cabe, la ultima se
        /// queda por debajo de su minimo: ya no hay de donde sacar.
        /// </summary>
        /// <param name="desired">Alturas deseadas; la de la ultima caja no se usa. No se modifica.</param>
        /// <returns>Las alturas que caben, una por caja.</returns>
        public static int[] Fit(int[] desired, int[] minimums, int total)
        {
            Validate(desired, minimums);

            int[] heights = (int[])desired.Clone();
            int last = heights.Length - 1;

            int others = 0;
            for (int i = 0; i < last; i++)
            {
                // Una altura guardada por debajo del minimo (un archivo editado a
                // mano, una version anterior) no se respeta.
                if (heights[i] < minimums[i]) heights[i] = minimums[i];
                others += heights[i];
            }
            heights[last] = total - others;

            if (heights[last] < minimums[last])
            {
                heights[last] += Take(heights, minimums, last - 1, 0, minimums[last] - heights[last]);
            }
            return heights;
        }

        /// <summary>
        /// Quita hasta <paramref name="amount"/> pixeles a las cajas que van de
        /// <paramref name="from"/> a <paramref name="to"/>, en ese orden y en
        /// cualquiera de los dos sentidos, sin bajar ninguna de su minimo.
        /// </summary>
        /// <returns>Lo que se pudo quitar.</returns>
        private static int Take(int[] heights, int[] minimums, int from, int to, int amount)
        {
            int step = from <= to ? 1 : -1;
            int taken = 0;

            for (int i = from; taken < amount; i += step)
            {
                int spare = heights[i] - minimums[i];
                if (spare > 0)
                {
                    int take = Math.Min(spare, amount - taken);
                    heights[i] -= take;
                    taken += take;
                }
                if (i == to) break;
            }
            return taken;
        }

        private static void Validate(int[] heights, int[] minimums)
        {
            if (heights == null) throw new ArgumentNullException("heights");
            if (minimums == null) throw new ArgumentNullException("minimums");
            if (heights.Length < 2 || minimums.Length != heights.Length)
            {
                throw new ArgumentException("Hacen falta al menos dos cajas, cada una con su minimo.");
            }
        }
    }
}
