using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LanShare.Tests
{
    /// <summary>
    /// Ejecutor de pruebas. Agrupa por bloques, cuenta y devuelve un codigo de
    /// salida distinto de cero si algo falla, para poder encadenarlo con la
    /// compilacion.
    /// </summary>
    public sealed class TestRunner
    {
        private sealed class Failure
        {
            public string Suite;
            public string Test;
            public string Message;
            public string Where;
        }

        private readonly List<Failure> _failures = new List<Failure>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private string _suite = "(sin bloque)";
        private int _suitePassed;
        private int _suiteTotal;
        private int _passed;
        private int _total;

        public void Suite(string name)
        {
            FlushSuite();
            _suite = name;
            _suitePassed = 0;
            _suiteTotal = 0;
        }

        public void Test(string name, Action body)
        {
            _total++;
            _suiteTotal++;

            try
            {
                body();
                _passed++;
                _suitePassed++;
            }
            catch (AssertionException ex)
            {
                Record(name, ex.Message, "");
            }
            catch (Exception ex)
            {
                Record(name, ex.GetType().Name + ": " + ex.Message, FirstFrame(ex));
            }
        }

        private void Record(string test, string message, string where)
        {
            Failure failure = new Failure();
            failure.Suite = _suite;
            failure.Test = test;
            failure.Message = message;
            failure.Where = where;
            _failures.Add(failure);
        }

        private static string FirstFrame(Exception ex)
        {
            if (string.IsNullOrEmpty(ex.StackTrace)) return "";
            string[] lines = ex.StackTrace.Split('\n');
            return lines.Length > 0 ? lines[0].Trim() : "";
        }

        private void FlushSuite()
        {
            if (_suiteTotal == 0) return;

            bool ok = _suitePassed == _suiteTotal;
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(ok ? "  OK   " : "  FALLA");
            Console.ResetColor();
            Console.WriteLine(" {0}  {1}/{2}", Pad(_suite, 46), _suitePassed, _suiteTotal);
        }

        private static string Pad(string value, int width)
        {
            if (value.Length >= width) return value;
            return value + new string('.', width - value.Length);
        }

        public int Report()
        {
            FlushSuite();

            Console.WriteLine();
            Console.WriteLine("===========================================================");

            if (_failures.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  {0} FALLO(S)", _failures.Count);
                Console.ResetColor();
                Console.WriteLine("===========================================================");

                foreach (Failure failure in _failures)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [{0}] {1}", failure.Suite, failure.Test);
                    Console.ResetColor();
                    Console.WriteLine("      {0}", failure.Message);
                    if (!string.IsNullOrEmpty(failure.Where)) Console.WriteLine("      {0}", failure.Where);
                }

                Console.WriteLine();
                Console.WriteLine("-----------------------------------------------------------");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  TODO CORRECTO");
                Console.ResetColor();
                Console.WriteLine("===========================================================");
            }

            Console.WriteLine("  {0} pruebas, {1} correctas, {2} fallidas, {3} ms",
                              _total, _passed, _failures.Count, _clock.ElapsedMilliseconds);
            Console.WriteLine();

            return _failures.Count == 0 ? 0 : 1;
        }
    }
}
