using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace DescagaCompronanteSRI.Validation;

public sealed class SriSchemaCatalog
{
    private static readonly Assembly assembly = typeof(SriSchemaCatalog).Assembly;
    private static readonly IReadOnlyDictionary<string, string> resources = assembly.GetManifestResourceNames()
        .Where(x => x.EndsWith(".xsd", StringComparison.Ordinal))
        .ToDictionary(x => x[(x.IndexOf("Schemas.", StringComparison.Ordinal) + 8)..], StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<XmlSchemaSet>> cache = new();
    public XmlSchemaSet? Get(string root, string version, out string name)
    {
        var prefix = root switch { "factura" => "factura", "notaCredito" => "NotaCredito", "notaDebito" => "NotaDebito",
            "liquidacionCompra" => "LiquidacionCompra", "guiaRemision" => "GuiaRemision", "comprobanteRetencion" => "ComprobanteRetencion", _ => "" };
        name = $"{prefix}_V{version}.xsd";
        return resources.ContainsKey(name) ? cache.GetOrAdd(name, file => new Lazy<XmlSchemaSet>(() => Load(file))).Value : null;
    }
    private static XmlSchemaSet Load(string file)
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        foreach (var name in new[] { "xmldsig-core-schema.xsd", file })
        {
            using var stream = assembly.GetManifestResourceStream(resources[name])!;
            using var textReader = new StreamReader(stream);
            var text = textReader.ReadToEnd();
            // Two official XSDs declare XML 1.1 but contain XML 1.0-compatible schema declarations.
            // Normalize only this trusted resource's declaration; downloaded documents remain untouched.
            text = System.Text.RegularExpressions.Regex.Replace(text, "^<\\?xml version=\"1\\.1\"", "<?xml version=\"1.0\"");
            using var source = new StringReader(text);
            using var reader = XmlReader.Create(source, SafeSriXml.Settings());
            set.Add(null, reader);
        }
        set.Compile();
        return set;
    }
}
