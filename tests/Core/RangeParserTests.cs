using System;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Interpretacion de la cabecera Range. Aqui vivio un fallo real:
    /// "bytes=-" se aceptaba y devolvia el archivo entero con un 206.
    /// </summary>
    public static class RangeParserTests
    {
        private const long Size = 1000;

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / RangeParser");

            runner.Test("sin cabecera no hay rango", delegate
            {
                RangeSpec spec = RangeParser.Parse(null, Size);
                Check.False(spec.Present, "Present con null");
                Check.False(spec.Satisfiable, "Satisfiable con null");
            });

            runner.Test("cabecera vacia no hay rango", delegate
            {
                Check.False(RangeParser.Parse("", Size).Present, "Present con vacio");
            });

            runner.Test("rango completo explicito", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=0-99", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(0L, spec.Start, "inicio");
                Check.Equal(99L, spec.End, "fin");
                Check.Equal(100L, spec.Length, "longitud");
            });

            runner.Test("sin fin llega hasta el final", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=500-", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(500L, spec.Start, "inicio");
                Check.Equal(999L, spec.End, "fin");
            });

            runner.Test("sufijo: los ultimos N bytes", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=-100", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(900L, spec.Start, "inicio");
                Check.Equal(999L, spec.End, "fin");
            });

            runner.Test("sufijo mayor que el archivo se recorta al principio", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=-5000", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(0L, spec.Start, "inicio");
                Check.Equal(999L, spec.End, "fin");
            });

            runner.Test("sufijo cero no designa nada", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=-0", Size);
                Check.True(spec.Present, "se reconoce la cabecera");
                Check.False(spec.Satisfiable, "pero no es satisfacible");
            });

            runner.Test("REGRESION: bytes=- no es un rango valido", delegate
            {
                // Antes matcheaba y devolvia el archivo entero como 206.
                RangeSpec spec = RangeParser.Parse("bytes=-", Size);
                Check.True(spec.Present, "se reconoce la cabecera");
                Check.False(spec.Satisfiable, "no designa ningun rango");
            });

            runner.Test("fin mas alla del archivo se recorta", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=0-5000", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(999L, spec.End, "fin recortado");
            });

            runner.Test("inicio fuera del archivo no es satisfacible", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=1000-", Size);
                Check.True(spec.Present, "se reconoce");
                Check.False(spec.Satisfiable, "no satisfacible");
            });

            runner.Test("inicio mayor que fin no es satisfacible", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=500-100", Size);
                Check.False(spec.Satisfiable, "no satisfacible");
            });

            runner.Test("ultimo byte suelto", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=999-999", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(1L, spec.Length, "un solo byte");
            });

            runner.Test("archivo vacio: ningun rango es satisfacible", delegate
            {
                Check.False(RangeParser.Parse("bytes=0-", 0).Satisfiable, "bytes=0- en vacio");
                Check.False(RangeParser.Parse("bytes=0-0", 0).Satisfiable, "bytes=0-0 en vacio");
                Check.False(RangeParser.Parse("bytes=-1", 0).Satisfiable, "sufijo en vacio");
            });

            runner.Test("texto no numerico se ignora", delegate
            {
                Check.False(RangeParser.Parse("bytes=abc-def", Size).Present, "letras");
                Check.False(RangeParser.Parse("kilobytes=0-99", Size).Present, "unidad desconocida");
                Check.False(RangeParser.Parse("0-99", Size).Present, "sin prefijo");
            });

            runner.Test("rangos multiples se ignoran y se sirve entero", delegate
            {
                // La norma permite responder con el recurso completo.
                RangeSpec spec = RangeParser.Parse("bytes=0-50,100-150", Size);
                Check.False(spec.Present, "no se reconoce como rango simple");
            });

            runner.Test("numeros desbordados no revientan", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=99999999999999999999-", Size);
                Check.False(spec.Satisfiable, "no satisfacible");
            });

            runner.Test("tolera espacios alrededor", delegate
            {
                RangeSpec spec = RangeParser.Parse("  bytes = 10 - 20  ", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(10L, spec.Start, "inicio");
                Check.Equal(20L, spec.End, "fin");
            });

            runner.Test("un solo byte al principio", delegate
            {
                RangeSpec spec = RangeParser.Parse("bytes=0-0", Size);
                Check.True(spec.Satisfiable, "satisfacible");
                Check.Equal(1L, spec.Length, "longitud");
            });
        }
    }
}
