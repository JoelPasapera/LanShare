using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LanShare.Net;

namespace LanShare.Ui
{
    /// <summary>
    /// Lista de puertos con su estado y, cuando se puede averiguar, el programa
    /// que los tiene ocupados.
    /// </summary>
    public sealed class PortPickerDialog : Form
    {
        private const int Inset = 14;

        private readonly ICollection<int> _otherSitePorts;
        private readonly ListView _list;
        private readonly Label _summary;
        private readonly TextBox _customPort;

        public int SelectedPort { get; private set; }

        public PortPickerDialog(ICollection<int> otherSitePorts)
        {
            _otherSitePorts = otherSitePorts;
            SelectedPort = -1;

            Text = "Puertos disponibles";
            // Redimensionable: con veinte filas y una altura fija solo se ven
            // trece, y las rutas de proceso pueden ser largas.
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(700, 480);
            MinimumSize = new Size(600, 380);

            Label hint = new Label();
            hint.Location = new Point(Inset, Inset);
            hint.Size = new Size(ClientSize.Width - Inset * 2, 32);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            hint.ForeColor = Color.FromArgb(85, 95, 110);
            hint.Font = new Font("Segoe UI", 8.5f);
            hint.Text = "Doble clic para elegir. Se comprueba tanto lo que ya escucha como los " +
                        "rangos que el sistema tiene reservados sin escuchar en ellos.";
            Controls.Add(hint);

            _list = new ListView();
            _list.Location = new Point(Inset, 52);
            _list.Size = new Size(ClientSize.Width - Inset * 2, ClientSize.Height - 52 - 78);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                           AnchorStyles.Left | AnchorStyles.Right;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.Font = new Font("Consolas", 9f);
            _list.Columns.Add("Puerto", 70);
            _list.Columns.Add("Estado", 165);
            _list.Columns.Add("Ocupado por", 190);
            _list.Columns.Add("Uso habitual", 220);
            _list.DoubleClick += OnPickClick;
            _list.Resize += OnListResize;
            Controls.Add(_list);

            int bottom = ClientSize.Height - 40;

            Label customLabel = new Label();
            customLabel.Location = new Point(Inset, bottom + 6);
            customLabel.Size = new Size(96, 18);
            customLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            customLabel.Text = "Otro puerto:";
            Controls.Add(customLabel);

            _customPort = new TextBox();
            _customPort.Location = new Point(Inset + 96, bottom + 3);
            _customPort.Size = new Size(64, 23);
            _customPort.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_customPort);

            Button check = MakeButton("Comprobar", Inset + 166, bottom, 88, OnCheckCustomClick,
                                      AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(check);

            _summary = new Label();
            _summary.Location = new Point(Inset + 262, bottom + 6);
            _summary.Size = new Size(160, 18);
            _summary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _summary.ForeColor = Color.FromArgb(85, 95, 110);
            _summary.Font = new Font("Segoe UI", 8.5f);
            Controls.Add(_summary);

            Button refresh = MakeButton("Actualizar", ClientSize.Width - Inset - 258, bottom, 80,
                                        OnRefreshClick, AnchorStyles.Bottom | AnchorStyles.Right);
            Button pick = MakeButton("Usar este", ClientSize.Width - Inset - 172, bottom, 84,
                                     OnPickClick, AnchorStyles.Bottom | AnchorStyles.Right);
            Button cancel = MakeButton("Cancelar", ClientSize.Width - Inset - 82, bottom, 82,
                                       null, AnchorStyles.Bottom | AnchorStyles.Right);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(refresh);
            Controls.Add(pick);
            Controls.Add(cancel);

            AcceptButton = pick;
            CancelButton = cancel;

            Load += OnDialogLoad;
        }

        private static Button MakeButton(string text, int x, int y, int width,
                                         EventHandler handler, AnchorStyles anchor)
        {
            Button button = new Button();
            button.Location = new Point(x, y);
            button.Size = new Size(width, 28);
            button.Anchor = anchor;
            button.Text = text;
            if (handler != null) button.Click += handler;
            return button;
        }

        /// <summary>
        /// La ultima columna absorbe el ancho sobrante. Sin esto aparece una
        /// barra horizontal en cuanto surge la vertical, porque esta se come
        /// parte del area util.
        /// </summary>
        private void OnListResize(object sender, EventArgs e)
        {
            int usado = 0;
            for (int i = 0; i < _list.Columns.Count - 1; i++) usado += _list.Columns[i].Width;

            int resto = _list.ClientSize.Width - usado - 4;
            if (resto > 80) _list.Columns[_list.Columns.Count - 1].Width = resto;
        }

        private void OnDialogLoad(object sender, EventArgs e)
        {
            Refresh(true);
            OnListResize(null, EventArgs.Empty);
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            Refresh(false);
        }

        /// <summary>
        /// El sondeo intenta reservar cada puerto y consulta la tabla de
        /// procesos, asi que puede tardar una fraccion de segundo.
        /// </summary>
        private void Refresh(bool selectFirstFree)
        {
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                List<PortInfo> ports = PortAvailability.Survey(_otherSitePorts);

                _list.BeginUpdate();
                _list.Items.Clear();

                int free = 0;
                ListViewItem firstFree = null;

                foreach (PortInfo info in ports)
                {
                    ListViewItem item = AddRow(info);
                    if (info.Status == PortStatus.Free)
                    {
                        free++;
                        if (firstFree == null) firstFree = item;
                    }
                }

                _list.EndUpdate();

                _summary.Text = free + (free == 1 ? " puerto libre" : " puertos libres") +
                                " de " + ports.Count;

                if (selectFirstFree && firstFree != null)
                {
                    firstFree.Selected = true;
                    firstFree.EnsureVisible();
                }
            }
            finally
            {
                Cursor = previous;
            }
        }

        private ListViewItem AddRow(PortInfo info)
        {
            ListViewItem item = new ListViewItem(info.Port.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(info.StatusText);
            item.SubItems.Add(info.Owner);
            item.SubItems.Add(info.CommonUse);
            item.Tag = info;
            item.ForeColor = info.Status == PortStatus.Free
                ? Color.FromArgb(20, 120, 45)
                : Color.Gray;

            _list.Items.Add(item);
            return item;
        }

        /// <summary>
        /// Permite consultar un puerto que no esta en la lista de habituales.
        /// Si ya figura, se salta a el en vez de duplicarlo.
        /// </summary>
        private void OnCheckCustomClick(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_customPort.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Escribe un puerto entre 1 y 65535.", "Puerto invalido",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (ListViewItem existing in _list.Items)
            {
                PortInfo info = (PortInfo)existing.Tag;
                if (info.Port != port) continue;
                existing.Selected = true;
                existing.EnsureVisible();
                _list.Focus();
                return;
            }

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                PortInfo info = PortAvailability.Describe(port, "consultado a mano",
                                                          _otherSitePorts,
                                                          PortAvailability.GetListeningPorts(),
                                                          PortAvailability.GetListenerOwners());
                ListViewItem item = AddRow(info);
                item.Selected = true;
                item.EnsureVisible();
                _list.Focus();
            }
            finally
            {
                Cursor = previous;
            }
        }

        private void OnPickClick(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) return;

            PortInfo info = (PortInfo)_list.SelectedItems[0].Tag;
            if (info.Status != PortStatus.Free)
            {
                string detalle = info.Owner.Length > 0 ? " (" + info.Owner + ")" : "";
                MessageBox.Show(this,
                    "Ese puerto no esta libre: " + info.StatusText + detalle + ".",
                    "Puerto no disponible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedPort = info.Port;
            DialogResult = DialogResult.OK;
        }
    }
}
