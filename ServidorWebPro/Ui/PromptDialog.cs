using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServidorWebPro.Ui
{
    /// <summary>
    /// Dialogo de una sola linea. WinForms no trae un InputBox, y arrastrar una
    /// dependencia solo para pedir un texto no compensa.
    /// </summary>
    public sealed class PromptDialog : Form
    {
        private readonly TextBox _input;

        public string Value { get { return _input.Text.Trim(); } }

        public PromptDialog(string title, string prompt, string initialValue)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 120);

            Label label = new Label();
            label.Location = new Point(14, 14);
            label.Size = new Size(392, 34);
            label.Text = prompt;
            Controls.Add(label);

            _input = new TextBox();
            _input.Location = new Point(14, 52);
            _input.Size = new Size(392, 23);
            _input.Text = initialValue ?? "";
            _input.SelectAll();
            Controls.Add(_input);

            Button ok = new Button();
            ok.Location = new Point(246, 84);
            ok.Size = new Size(78, 26);
            ok.Text = "Aceptar";
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Location = new Point(330, 84);
            cancel.Size = new Size(78, 26);
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            ActiveControl = _input;
        }

        public static string Ask(IWin32Window owner, string title, string prompt, string initialValue)
        {
            using (PromptDialog dialog = new PromptDialog(title, prompt, initialValue))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Value : null;
            }
        }
    }
}
