using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace LanShare.Core
{
    /// <summary>Un mensaje de consola tal como lo manda el navegador.</summary>
    public sealed class ConsoleEntry
    {
        /// <summary>error, warn, info, log o debug.</summary>
        public readonly string Level;
        public readonly string Text;

        /// <summary>Ubicacion ya acortada por el script: "/js/app.js:42:7".</summary>
        public readonly string Where;

        /// <summary>Pila del error, si lo habia, en el formato del navegador.</summary>
        public readonly string Stack;

        /// <summary>Veces seguidas que se repitio: el script agrupa los iguales.</summary>
        public readonly int Count;

        public ConsoleEntry(string level, string text, string where, string stack, int count)
        {
            Level = level ?? "";
            Text = text ?? "";
            Where = where ?? "";
            Stack = stack ?? "";
            Count = count < 1 ? 1 : count;
        }
    }

    /// <summary>
    /// Lote del script de la consola remota:
    /// {"p": pagina, "d": omitidos, "e": [{"l", "m", "s", "k", "n"}, ...]}.
    ///
    /// Cualquier equipo de la red puede mandar lo que quiera a /__consola, asi
    /// que nada se da por supuesto: un campo con un tipo inesperado se toma
    /// como vacio y un cuerpo que no sea un objeto JSON se rechaza entero.
    /// </summary>
    public sealed class ConsoleReport
    {
        /// <summary>Mensajes por lote. El script nunca manda mas de 100.</summary>
        public const int MaxEntries = 200;

        public readonly string Page;

        /// <summary>Mensajes que el navegador descarto por tener la cola llena.</summary>
        public readonly int Dropped;

        public readonly List<ConsoleEntry> Entries;

        private ConsoleReport(string page, int dropped, List<ConsoleEntry> entries)
        {
            Page = page;
            Dropped = dropped;
            Entries = entries;
        }

        /// <returns>null si el cuerpo no es un lote interpretable.</returns>
        public static ConsoleReport Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 256 * 1024;

                // El lote tiene tres niveles; un anidamiento profundo solo
                // puede venir de alguien buscando agotar la pila.
                serializer.RecursionLimit = 16;
                root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception) { return null; }

            if (root == null) return null;

            List<ConsoleEntry> entries = new List<ConsoleEntry>();
            object raw;
            IList list = root.TryGetValue("e", out raw) ? raw as IList : null;
            if (list != null)
            {
                foreach (object item in list)
                {
                    if (entries.Count >= MaxEntries) break;

                    Dictionary<string, object> fields = item as Dictionary<string, object>;
                    if (fields == null) continue;

                    entries.Add(new ConsoleEntry(Text(fields, "l"), Text(fields, "m"), Text(fields, "s"),
                                                 Text(fields, "k"), Number(fields, "n")));
                }
            }

            return new ConsoleReport(Text(root, "p"), Number(root, "d"), entries);
        }

        private static string Text(Dictionary<string, object> fields, string key)
        {
            object value;
            if (!fields.TryGetValue(key, out value)) return "";
            return value as string ?? "";
        }

        /// <summary>Entero no negativo; cualquier otra cosa cuenta como cero.</summary>
        private static int Number(Dictionary<string, object> fields, string key)
        {
            object value;
            if (!fields.TryGetValue(key, out value) || value == null) return 0;
            if (value is string || value is bool) return 0;

            try
            {
                decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number <= 0) return 0;
                return number >= int.MaxValue ? int.MaxValue : (int)number;
            }
            catch (Exception) { return 0; }
        }
    }
}
