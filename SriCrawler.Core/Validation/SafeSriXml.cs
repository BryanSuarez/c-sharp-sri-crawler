using System.Xml;
using System.Xml.Linq;

namespace DescagaCompronanteSRI.Validation;

internal static class SafeSriXml
{
    public const int MaxCharacters = 20 * 1024 * 1024;
    public static XmlReaderSettings Settings(long maxCharacters = MaxCharacters) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = maxCharacters,
        MaxCharactersFromEntities = 0, Async = false
    };
    public static XDocument Read(string xml)
    {
        using var text = new StringReader(xml.TrimStart('\uFEFF'));
        using var reader = XmlReader.Create(text, Settings());
        return XDocument.Load(reader);
    }
    public static XDocument Read(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings());
        return XDocument.Load(reader);
    }
    public static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>
    {
        ["factura"] = "01", ["liquidacionCompra"] = "03", ["notaCredito"] = "04",
        ["notaDebito"] = "05", ["guiaRemision"] = "06", ["comprobanteRetencion"] = "07"
    };
    public static string Extract(string response)
    {
        var outer = Read(response);
        if (outer.Root is null) throw new XmlException("Missing document root.");
        if (Codes.ContainsKey(outer.Root.Name.LocalName) && outer.Root.Name.NamespaceName == "") return response;
        if (outer.Root.Name.LocalName is not ("autorizacion" or "Authorization" or "partial-response"))
            throw new XmlException("Unexpected document root.");
        var receipts = outer.Descendants("comprobante").ToArray();
        if (receipts.Length != 1 || receipts[0].HasElements) throw new XmlException("Missing or ambiguous receipt.");
        using var source = new StringReader(response.TrimStart('\uFEFF'));
        using var lexicalReader = new XmlTextReader(source) { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, Normalization = false };
        var lexical = XDocument.Load(lexicalReader);
        var content = lexical.Descendants("comprobante").Single().Value;
        var inner = Read(content);
        if (inner.Root is null || !Codes.ContainsKey(inner.Root.Name.LocalName) || inner.Root.Name.NamespaceName != "")
            throw new XmlException("Unexpected receipt root.");
        return content;
    }
}
