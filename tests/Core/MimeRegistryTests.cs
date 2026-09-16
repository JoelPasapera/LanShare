using System;
using LanShare.Core;

namespace LanShare.Tests
{
    public static class MimeRegistryTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / MimeRegistry");

            runner.Test("extensiones conocidas", delegate
            {
                Check.StartsWith(MimeRegistry.Resolve("a.html"), "text/html", "html");
                Check.StartsWith(MimeRegistry.Resolve("a.css"), "text/css", "css");
                Check.StartsWith(MimeRegistry.Resolve("a.js"), "text/javascript", "js");
                Check.StartsWith(MimeRegistry.Resolve("a.json"), "application/json", "json");
                Check.Equal("image/png", MimeRegistry.Resolve("a.png"), "png");
                Check.Equal("application/wasm", MimeRegistry.Resolve("a.wasm"), "wasm");
                Check.Equal("font/woff2", MimeRegistry.Resolve("a.woff2"), "woff2");
            });

            runner.Test("los tipos de texto declaran utf-8", delegate
            {
                Check.Contains(MimeRegistry.Resolve("a.html"), "charset=utf-8", "html");
                Check.Contains(MimeRegistry.Resolve("a.js"), "charset=utf-8", "js");
                Check.Contains(MimeRegistry.Resolve("a.json"), "charset=utf-8", "json");
            });

            runner.Test("la extension no distingue mayusculas", delegate
            {
                Check.Equal(MimeRegistry.Resolve("a.png"), MimeRegistry.Resolve("A.PNG"), "png");
                Check.Equal(MimeRegistry.Resolve("a.html"), MimeRegistry.Resolve("A.HtMl"), "html");
            });

            runner.Test("desconocida y sin extension caen al generico", delegate
            {
                Check.Equal(MimeRegistry.Fallback, MimeRegistry.Resolve("a.qwerty"), "desconocida");
                Check.Equal(MimeRegistry.Fallback, MimeRegistry.Resolve("LICENSE"), "sin extension");
            });

            runner.Test("IsHtml solo para html", delegate
            {
                Check.True(MimeRegistry.IsHtml("text/html; charset=utf-8"), "con charset");
                Check.True(MimeRegistry.IsHtml("text/html"), "sin charset");
                Check.False(MimeRegistry.IsHtml("text/plain; charset=utf-8"), "texto plano");
                Check.False(MimeRegistry.IsHtml("application/json"), "json");
                Check.False(MimeRegistry.IsHtml(null), "nulo");
            });

            runner.Test("una ruta completa tambien resuelve", delegate
            {
                Check.StartsWith(MimeRegistry.Resolve(@"C:\web\css\app.css"), "text/css", "ruta completa");
            });
        }
    }
}
