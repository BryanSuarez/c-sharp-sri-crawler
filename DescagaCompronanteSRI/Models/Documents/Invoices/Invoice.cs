using System;
using System.Xml.Serialization;
using System.Collections.Generic;
namespace DescagaCompronanteSRI.Models.Documents.Invoices
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class InvoiceTaxInformation
    {
        [XmlElement(ElementName = "ambiente")]
        public string Environment { get; set; } = "";
        [XmlElement(ElementName = "tipoEmision")]
        public string IssueType { get; set; } = "";
        [XmlElement(ElementName = "razonSocial")]
        public string BusinessName { get; set; } = "";
        [XmlElement(ElementName = "ruc")]
        public string TaxpayerId { get; set; } = "";
        [XmlElement(ElementName = "claveAcceso")]
        public string AccessKey { get; set; } = "";
        [XmlElement(ElementName = "codDoc")]
        public string DocumentCode { get; set; } = "";
        [XmlElement(ElementName = "estab")]
        public string EstablishmentCode { get; set; } = "";
        [XmlElement(ElementName = "ptoEmi")]
        public string IssuePointCode { get; set; } = "";
        [XmlElement(ElementName = "secuencial")]
        public string Sequence { get; set; } = "";
        [XmlElement(ElementName = "dirMatriz")]
        public string HeadOfficeAddress { get; set; } = "";
        [XmlElement(ElementName = "agenteRetencion")]
        public string WithholdingAgent { get; set; } = "";
        [XmlElement(ElementName = "contribuyenteRimpe")]
        public string RimpeTaxpayer { get; set; } = "";
    }

    [XmlRoot(ElementName = "totalImpuesto")]
    public class InvoiceTotalTax
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string PercentageCode { get; set; } = "";
        [XmlElement(ElementName = "baseImponible")]
        public string TaxableBase { get; set; } = "";
        [XmlElement(ElementName = "tarifa")]
        public string Rate { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "totalConImpuestos")]
    public class InvoiceTaxTotals
    {
        [XmlElement(ElementName = "totalImpuesto")]
        public InvoiceTotalTax? TotalTax { get; set; }
    }

    [XmlRoot(ElementName = "pago")]
    public class InvoicePayment
    {
        [XmlElement(ElementName = "formaPago")]
        public string PaymentMethod { get; set; } = "";
        [XmlElement(ElementName = "total")]
        public string Total { get; set; } = "";
    }

    [XmlRoot(ElementName = "pagos")]
    public class InvoicePayments
    {
        [XmlElement(ElementName = "pago")]
        public InvoicePayment? Payment { get; set; }
    }

    [XmlRoot(ElementName = "infoFactura")]
    public class InvoiceInformation
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string IssueDate { get; set; } = "";
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string EstablishmentAddress { get; set; } = "";
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string SpecialTaxpayer { get; set; } = "";
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string AccountingRequired { get; set; } = "";
        [XmlElement(ElementName = "tipoIdentificacionComprador")]
        public string BuyerIdentificationType { get; set; } = "";
        [XmlElement(ElementName = "razonSocialComprador")]
        public string BuyerBusinessName { get; set; } = "";
        [XmlElement(ElementName = "identificacionComprador")]
        public string BuyerIdentification { get; set; } = "";
        [XmlElement(ElementName = "direccionComprador")]
        public string BuyerAddress { get; set; } = "";
        [XmlElement(ElementName = "totalSinImpuestos")]
        public string TotalWithoutTaxes { get; set; } = "";
        [XmlElement(ElementName = "totalDescuento")]
        public string TotalDiscount { get; set; } = "";
        [XmlElement(ElementName = "totalConImpuestos")]
        public InvoiceTaxTotals? TaxTotals { get; set; }
        [XmlElement(ElementName = "propina")]
        public string Tip { get; set; } = "";
        [XmlElement(ElementName = "importeTotal")]
        public string TotalAmount { get; set; } = "";
        [XmlElement(ElementName = "moneda")]
        public string Currency { get; set; } = "";
        [XmlElement(ElementName = "pagos")]
        public InvoicePayments? Payments { get; set; }
    }

    [XmlRoot(ElementName = "impuesto")]
    public class InvoiceTax
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string PercentageCode { get; set; } = "";
        [XmlElement(ElementName = "tarifa")]
        public string Rate { get; set; } = "";
        [XmlElement(ElementName = "baseImponible")]
        public string TaxableBase { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "impuestos")]
    public class InvoiceTaxes
    {
        [XmlElement(ElementName = "impuesto")]
        public List<InvoiceTax> Tax { get; set; } = [];
    }

    [XmlRoot(ElementName = "detalle")]
    public class InvoiceItem
    {
        [XmlElement(ElementName = "codigoPrincipal")]
        public string PrimaryCode { get; set; } = "";
        [XmlElement(ElementName = "codigoAuxiliar")]
        public string AuxiliaryCode { get; set; } = "";
        [XmlElement(ElementName = "descripcion")]
        public string Description { get; set; } = "";
        [XmlElement(ElementName = "cantidad")]
        public string Quantity { get; set; } = "";
        [XmlElement(ElementName = "precioUnitario")]
        public string UnitPrice { get; set; } = "";
        [XmlElement(ElementName = "descuento")]
        public string Discount { get; set; } = "";
        [XmlElement(ElementName = "precioTotalSinImpuesto")]
        public string TotalPriceWithoutTax { get; set; } = "";
        [XmlElement(ElementName = "impuestos")]
        public InvoiceTaxes? Taxes { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class InvoiceItems
    {
        [XmlElement(ElementName = "detalle")]
        public List<InvoiceItem> Items { get; set; } = [];
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class InvoiceAdditionalField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlText]
        public string Text { get; set; } = "";
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InvoiceAdditionalInformation
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<InvoiceAdditionalField> AdditionalField { get; set; } = [];
    }

    [XmlRoot(ElementName = "factura")]
    public class Invoice
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InvoiceTaxInformation? TaxInformation { get; set; }
        [XmlElement(ElementName = "infoFactura")]
        public InvoiceInformation? InvoiceInformation { get; set; }
        [XmlElement(ElementName = "detalles")]
        public InvoiceItems? Items { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InvoiceAdditionalInformation? AdditionalInformation { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; } = "";
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; } = "";
    }

}
