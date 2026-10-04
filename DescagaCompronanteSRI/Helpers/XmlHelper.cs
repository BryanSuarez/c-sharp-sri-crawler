using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Services;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace DescagaCompronanteSRI.Helpers
{
    public static class XmlHelper
    {
        /// <summary>
        /// Extrae el XML del comprobante desde la respuesta JSF
        /// (puede venir envuelto en CDATA o como elemento directo).
        /// </summary>
        public static string ExtraerXmlComprobante(string respuestaJsf)
        {
            try
            {
                var el = XDocument.Parse(respuestaJsf)
                                  .Descendants("comprobante")
                                  .FirstOrDefault();
                if (el is null) return "";

                // CDATA
                var cdata = el.DescendantNodes().OfType<XCData>().FirstOrDefault();
                if (cdata != null) return cdata.Value;

                // Texto plano que ya es XML
                string inner = el.Value;
                if (!string.IsNullOrWhiteSpace(inner) && inner.TrimStart().StartsWith("<"))
                    return inner;

                return el.Elements().FirstOrDefault()?.ToString() ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Deserializa el XML del comprobante y lo asigna al campo correcto del DTO.
        /// </summary>
        public static void DeserializarEnComprobante(
            ReceivedDocumentResponse comp, string xmlContent, DocumentType documentType)
        {
            using var reader = new StringReader(xmlContent);

            switch (documentType)
            {
                case DocumentType.Invoice:
                    comp.Factura = Deserialize<FacturaXML>(xmlContent);
                    break;
                case DocumentType.PurchaseSettlement:
                    comp.Liquidacion = Deserialize<LiquidacionCompra>(xmlContent);
                    break;
                case DocumentType.CreditNote:
                    comp.NotaCredito = Deserialize<NotaCredito>(xmlContent);
                    break;
                case DocumentType.DebitNote:
                    comp.NotaDebito = Deserialize<NotaDebito>(xmlContent);
                    break;
                case DocumentType.Withholding:
                    comp.Retencion = Deserialize<ComprobanteRetencion>(xmlContent);
                    break;
                    // GuiaRemision (5) no tiene deserialización extra por ahora
            }
        }

        private static T? Deserialize<T>(string xml) where T : class
        {
            try
            {
                using var r = new StringReader(xml);
                return (T?)new XmlSerializer(typeof(T)).Deserialize(r);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [XML] Error deserializando {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }
    }
}