using System;
using System.Collections.Generic;
using System.Globalization;

namespace LanShare.Tests
{
    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    /// <summary>
    /// Aserciones minimas. No hay framework externo a proposito: el proyecto
    /// entero se compila con el csc que trae Windows, y las pruebas tambien.
    /// </summary>
    public static class Check
    {
        public static void True(bool condition, string what)
        {
            if (!condition) throw new AssertionException(what + ": se esperaba verdadero");
        }

        public static void False(bool condition, string what)
        {
            if (condition) throw new AssertionException(what + ": se esperaba falso");
        }

        public static void Equal(object expected, object actual, string what)
        {
            if (!object.Equals(expected, actual))
            {
                throw new AssertionException(string.Format(
                    "{0}: se esperaba <{1}> y llego <{2}>", what, Show(expected), Show(actual)));
            }
        }

        public static void NotEqual(object unexpected, object actual, string what)
        {
            if (object.Equals(unexpected, actual))
            {
                throw new AssertionException(string.Format(
                    "{0}: no se esperaba <{1}>", what, Show(unexpected)));
            }
        }

        public static void Null(object value, string what)
        {
            if (value != null) throw new AssertionException(what + ": se esperaba nulo, llego <" + Show(value) + ">");
        }

        public static void NotNull(object value, string what)
        {
            if (value == null) throw new AssertionException(what + ": no se esperaba nulo");
        }

        public static void Empty(string value, string what)
        {
            if (!string.IsNullOrEmpty(value))
                throw new AssertionException(what + ": se esperaba vacio, llego <" + value + ">");
        }

        public static void NotEmpty(string value, string what)
        {
            if (string.IsNullOrEmpty(value)) throw new AssertionException(what + ": se esperaba con contenido");
        }

        public static void Contains(string haystack, string needle, string what)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            {
                throw new AssertionException(string.Format(
                    "{0}: <{1}> deberia contener <{2}>", what, Show(haystack), needle));
            }
        }

        public static void DoesNotContain(string haystack, string needle, string what)
        {
            if (haystack != null && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0)
            {
                throw new AssertionException(string.Format(
                    "{0}: <{1}> NO deberia contener <{2}>", what, Show(haystack), needle));
            }
        }

        public static void StartsWith(string value, string prefix, string what)
        {
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new AssertionException(string.Format(
                    "{0}: <{1}> deberia empezar por <{2}>", what, Show(value), prefix));
            }
        }

        public static void BytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (expected == null || actual == null)
            {
                if (expected != actual) throw new AssertionException(what + ": uno de los dos es nulo");
                return;
            }

            if (expected.Length != actual.Length)
            {
                throw new AssertionException(string.Format(
                    "{0}: longitudes distintas, {1} frente a {2}", what, expected.Length, actual.Length));
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    throw new AssertionException(string.Format(
                        "{0}: difieren en el byte {1}, {2} frente a {3}", what, i, expected[i], actual[i]));
                }
            }
        }

        public static void InRange(long value, long min, long max, string what)
        {
            if (value < min || value > max)
            {
                throw new AssertionException(string.Format(
                    "{0}: {1} fuera del rango [{2}, {3}]", what, value, min, max));
            }
        }

        public static void Throws(Action action, string what)
        {
            try
            {
                action();
            }
            catch (AssertionException) { throw; }
            catch (Exception) { return; }

            throw new AssertionException(what + ": se esperaba una excepcion y no hubo ninguna");
        }

        public static void DoesNotThrow(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new AssertionException(what + ": no se esperaba excepcion y llego " +
                                             ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string Show(object value)
        {
            if (value == null) return "null";
            if (value is string) return (string)value;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
