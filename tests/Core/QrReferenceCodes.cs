using System;

namespace LanShare.Tests
{
    /// <summary>
    /// Codigos QR de referencia que atan las pruebas a un lector real.
    ///
    /// Los genero el codificador ya corregido (portado a Python para poder
    /// pasarlo por OpenCV) y los leyeron los dos detectores de OpenCV 5.0,
    /// QRCodeDetector y QRCodeDetectorAruco. Ademas coinciden modulo a modulo
    /// con el codificador QR de OpenCV usando la misma mascara, salvo los siete
    /// bits sobrantes del final, que la norma deja en claro y que los lectores
    /// descartan.
    ///
    /// Sin una referencia externa, el codificador y el decodificador de las
    /// pruebas pueden compartir un error y darse la razon el uno al otro: es lo
    /// que paso con los bits de formato.
    /// </summary>
    internal static class QrReferenceCodes
    {
        public sealed class Reference
        {
            public readonly string Text;
            public readonly int Version;
            public readonly int Mask;
            public readonly string[] Rows;

            public Reference(string text, int version, int mask, string[] rows)
            {
                Text = text;
                Version = version;
                Mask = mask;
                Rows = rows;
            }
        }

        /// <summary>Leidos por OpenCV. '#' es oscuro y '.' claro, una cadena por fila.</summary>
        public static readonly Reference[] Readable =
        {
            new Reference("http://192.168.1.5:3000/", 2, 5, new string[] {
                "#######...##.#..#.#######",
                "#.....#.#.##..#...#.....#",
                "#.###.#.#...#.#.#.#.###.#",
                "#.###.#.#########.#.###.#",
                "#.###.#..#...##...#.###.#",
                "#.....#...####..#.#.....#",
                "#######.#.#.#.#.#.#######",
                "........#..##............",
                "#.....#.##.###.####..###.",
                "#...##..##..#..###..####.",
                "....#.#...#..#.#..##.#.##",
                "....#.....#.#.###...##..#",
                "..##.####.##..#.###.....#",
                "#..###...#..#.##.......#.",
                "#.....#.##.....#.#.#.#.##",
                "#..##..#....#.#.#...#.#.#",
                "#.##..#.##..##..#####.#..",
                "........###...###...#.#..",
                "#######..##.###.#.#.##..#",
                "#.....#...##....#...#...#",
                "#.###.#..#.###.########..",
                "#.###.#..##.#.....##.#.##",
                "#.###.#..##......#....#.#",
                "#.....#....#...##.###...#",
                "#######.#...#.###.#..#..#",
            }),

            new Reference("http://192.168.18.42:8080/__subir", 3, 2, new string[] {
                "#######.....#..##..#..#######",
                "#.....#..##.......#...#.....#",
                "#.###.#.#.##..#.#.#...#.###.#",
                "#.###.#.#..##...#####.#.###.#",
                "#.###.#.#..#####.#.#..#.###.#",
                "#.....#.#.#######.#...#.....#",
                "#######.#.#.#.#.#.#.#.#######",
                "........###.##.#..##.........",
                "#.#####....#..##.####.#####..",
                ".##....#.####..##..#..#.#...#",
                "####.###..#.......#....##....",
                "...#....#.#.#.#.#.##.##.#..#.",
                "#.#.#.#.##.#.....##.##.#.##..",
                ".##.#...###.####.###..#.#.#.#",
                ".##.#.#.####.###.#...##...#..",
                "####...####..#......##.#...#.",
                "..##.##.#..#..###...##....#..",
                "##......#.##....##.#.##.###.#",
                "#.#.#.##..#....###....#..##..",
                "#.###......##.##...####.#..#.",
                "#.#####..#.........######.###",
                "........###.###.##..#...#####",
                "#######..#######.#.##.#.###..",
                "#.....#.##...#......#...#....",
                "#.###.#.#####.##.#..########.",
                "#.###.#.##..##..#....#.#...##",
                "#.###.#.#.##.#.####.#.#.##.#.",
                "#.....#..##.#####..###.##..#.",
                "#######.#####..###.......##..",
            }),
        };

        /// <summary>
        /// Lo que generaba LanShare para la primera URL de <see cref="Readable"/>
        /// con los bits de formato traspuestos (fila por columna). Ni OpenCV ni
        /// ningun movil lo leian.
        /// </summary>
        public static readonly string[] TransposedFormat =
        {
            "#######.#.....#...#######",
            "#.....#..###..##..#.....#",
            "#.###.#.#...#.#.#.#.###.#",
            "#.###.#.#..###....#.###.#",
            "#.###.#.##...##...#.###.#",
            "#.....#.######.##.#.....#",
            "#######.#.#.#.#.#.#######",
            ".........#.##..#.........",
            "..######.#.###.##..####.#",
            "#.##.#....#.#.#..#.....#.",
            "....#.#...#..#.#..##.#.##",
            "...##....##.#.#.#...#...#",
            ".#.##.#......#....###.###",
            "#...##......#.#......#.#.",
            "#.....#.##.....#.#.#.#.##",
            "#.#....####.#..#.....#..#",
            "#.##..#.##..##..#####.#..",
            "........#.#...#.#...###..",
            "#######.##.##...#.#.#####",
            "#.....#.####...##...##..#",
            "#.###.#.##.###.########..",
            "#.###.#.#...#.###.###.###",
            "#.###.#.###......#....#.#",
            "#.....#..#.#....#.####..#",
            "#######...####.#.########",
        };

        /// <summary>Convierte las filas en una matriz [fila, columna]; true es oscuro.</summary>
        public static bool[,] Parse(string[] rows)
        {
            if (rows == null) throw new ArgumentNullException("rows");

            int size = rows.Length;
            bool[,] modules = new bool[size, size];

            for (int row = 0; row < size; row++)
            {
                string line = rows[row];
                if (line == null || line.Length != size)
                {
                    throw new ArgumentException("La fila " + row + " no mide " + size + " modulos.", "rows");
                }

                for (int column = 0; column < size; column++)
                {
                    char module = line[column];
                    if (module != '#' && module != '.')
                    {
                        throw new ArgumentException("Caracter no valido en la fila " + row + ": " + module, "rows");
                    }
                    modules[row, column] = module == '#';
                }
            }

            return modules;
        }
    }
}
