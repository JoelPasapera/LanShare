using System;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Cajas apiladas en un contenedor, todas con Dock=Top salvo la ultima, que
    /// rellena lo que sobra. Traduce a controles lo que calcula PaneHeights.
    ///
    /// Guarda la altura que eligio el usuario aparte de la que se ve: si la
    /// ventana se achica, las cajas encogen hasta su minimo, y si vuelve a
    /// haber sitio recuperan la altura elegida en vez de quedarse aplastadas.
    /// </summary>
    internal sealed class PaneStack
    {
        /// <summary>Tope de cordura para una altura leida de la configuracion.</summary>
        private const int MaxHeight = 4000;

        private readonly Control _container;
        private readonly Control[] _panes;
        private readonly int[] _minimums;
        private readonly int[] _defaults;

        /// <summary>Alturas elegidas, una por caja; la de la ultima no se usa.</summary>
        private readonly int[] _desired;

        private int[] _dragStart;
        private int _dragBoundary = -1;
        private bool _dragMoved;

        /// <summary>El usuario termino de cambiar el reparto: es el momento de guardarlo.</summary>
        public event EventHandler Committed;

        /// <param name="container">Contenedor donde estan acopladas las cajas y sus divisores.</param>
        /// <param name="panes">Cajas de arriba abajo, con su altura de fabrica ya puesta; la ultima rellena.</param>
        /// <param name="minimums">Altura minima de cada caja.</param>
        public PaneStack(Control container, Control[] panes, int[] minimums)
        {
            if (container == null) throw new ArgumentNullException("container");
            if (panes == null || minimums == null || panes.Length < 2 || minimums.Length != panes.Length)
            {
                throw new ArgumentException("Hacen falta al menos dos cajas, cada una con su minimo.");
            }

            _container = container;
            _panes = panes;
            _minimums = minimums;
            _defaults = new int[panes.Length - 1];
            _desired = new int[panes.Length];

            for (int i = 0; i < _defaults.Length; i++)
            {
                _defaults[i] = panes[i].Height;
                _desired[i] = panes[i].Height;
            }

            container.Resize += OnContainerResize;
        }

        /// <summary>Conecta un divisor al limite que hay debajo de la caja <paramref name="boundary"/>.</summary>
        public void Attach(PaneGrip grip, int boundary)
        {
            if (grip == null) throw new ArgumentNullException("grip");
            if (boundary < 0 || boundary > _panes.Length - 2) throw new ArgumentOutOfRangeException("boundary");

            grip.DragStarted += delegate { BeginDrag(boundary); };
            grip.Dragged += delegate(object sender, GripDragEventArgs e) { DragTo(e.Offset); };
            grip.DragCompleted += delegate { EndDrag(); };
            grip.ResetRequested += delegate { Reset(); };
        }

        /// <summary>Alturas elegidas para las cajas, sin la ultima, que rellena.</summary>
        public int[] GetHeights()
        {
            int[] heights = new int[_defaults.Length];
            Array.Copy(_desired, heights, heights.Length);
            return heights;
        }

        /// <summary>
        /// Restaura un reparto guardado. Un valor que falte, cero, por debajo del
        /// minimo o absurdo deja la altura de fabrica de esa caja.
        /// </summary>
        public void SetHeights(params int[] heights)
        {
            for (int i = 0; i < _defaults.Length; i++)
            {
                int value = heights != null && i < heights.Length ? heights[i] : 0;
                _desired[i] = value >= _minimums[i] && value <= MaxHeight ? value : _defaults[i];
            }
            Arrange();
        }

        private void Reset()
        {
            Array.Copy(_defaults, _desired, _defaults.Length);
            Arrange();
            Raise(Committed);
        }

        private void BeginDrag(int boundary)
        {
            _dragStart = Current();
            _dragBoundary = boundary;
            _dragMoved = false;
        }

        private void DragTo(int offset)
        {
            if (_dragBoundary < 0) return;

            // Siempre desde las alturas del principio: asi el limite vuelve con
            // el raton aunque por el camino se haya topado con un minimo.
            int[] heights = (int[])_dragStart.Clone();
            PaneHeights.MoveBoundary(heights, _minimums, _dragBoundary, offset);
            Apply(heights);
            if (offset != 0) _dragMoved = true;
        }

        private void EndDrag()
        {
            if (_dragBoundary < 0) return;
            _dragBoundary = -1;

            // Un clic sin arrastrar no es elegir: con la ventana pequena, las
            // alturas que se ven estan encogidas y no deben pasar a ser las elegidas.
            if (!_dragMoved) return;

            int[] current = Current();
            Array.Copy(current, _desired, _defaults.Length);
            Raise(Committed);
        }

        private void OnContainerResize(object sender, EventArgs e)
        {
            if (_dragBoundary < 0) Arrange();
        }

        /// <summary>Encaja el reparto elegido en el espacio que hay ahora.</summary>
        private void Arrange()
        {
            Apply(PaneHeights.Fit(_desired, _minimums, Available()));
        }

        /// <summary>Alturas que se ven ahora; la ultima es lo que le queda a la que rellena.</summary>
        private int[] Current()
        {
            int[] heights = new int[_panes.Length];
            int last = heights.Length - 1;
            int used = 0;

            for (int i = 0; i < last; i++)
            {
                heights[i] = _panes[i].Height;
                used += heights[i];
            }
            heights[last] = Available() - used;
            return heights;
        }

        /// <summary>
        /// Altura para repartir entre las cajas: la del contenedor menos su
        /// relleno y lo que ocupan los demas controles acoplados arriba
        /// (divisores, botones, barras). No se mira Visible: mientras la ventana
        /// no se ha mostrado, todos sus controles dicen no ser visibles.
        /// </summary>
        private int Available()
        {
            int others = 0;
            foreach (Control child in _container.Controls)
            {
                if (child.Dock != DockStyle.Top) continue;
                if (Array.IndexOf(_panes, child) >= 0) continue;
                others += child.Height;
            }
            return _container.ClientSize.Height - _container.Padding.Vertical - others;
        }

        private void Apply(int[] heights)
        {
            _container.SuspendLayout();
            try
            {
                for (int i = 0; i < heights.Length - 1; i++)
                {
                    if (_panes[i].Height != heights[i]) _panes[i].Height = heights[i];
                }
            }
            finally
            {
                _container.ResumeLayout(true);
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
