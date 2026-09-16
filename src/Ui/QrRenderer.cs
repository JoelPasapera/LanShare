using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LanShare.Core;

namespace LanShare.Ui
{
    /// <summary>
    /// Dibuja la matriz de un codigo QR en un mapa de bits.
    ///
    /// El factor de escala es entero a proposito: si un modulo cayera sobre
    /// pixeles fraccionarios, el suavizado difuminaria los bordes y muchos
    /// lectores dejarian de reconocerlo.
    /// </summary>
    public static class QrRenderer
    {
        /// <summary>Zona de silencio obligatoria: cuatro modulos por lado.</summary>
        private const int QuietZone = 4;

        public static Bitmap Render(QrCode code, int targetPixels)
        {
            if (code == null) throw new ArgumentNullException("code");

            int totalModules = code.Size + QuietZone * 2;
            int scale = Math.Max(1, targetPixels / totalModules);
            int side = totalModules * scale;

            Bitmap bitmap = new Bitmap(side, side, PixelFormat.Format32bppPArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.Clear(Color.White);

                using (SolidBrush brush = new SolidBrush(Color.Black))
                {
                    for (int row = 0; row < code.Size; row++)
                    {
                        for (int column = 0; column < code.Size; column++)
                        {
                            if (!code[row, column]) continue;
                            graphics.FillRectangle(brush,
                                (column + QuietZone) * scale,
                                (row + QuietZone) * scale,
                                scale, scale);
                        }
                    }
                }
            }

            return bitmap;
        }
    }
}
