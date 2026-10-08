/*
 Licensed under the Apache License, Version 2.0

 http://www.apache.org/licenses/LICENSE-2.0
 */
using System;
using System.Xml.Serialization;
using System.Collections.Generic;
namespace DescagaCompronanteSRI.Models.Documents.CreditNotes
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class CreditNoteTaxInformation
    {
        [XmlElement(ElementName = "ambiente")]
        public string Environment { get; set; } = "";
        [XmlElement(ElementName = "tipoEmision")]
        public string IssueType { get; set; } = "";
        [XmlElement(ElementName = "razonSocial")]
        public string BusinessName { get; set; } = "";
        [XmlElement(ElementName = "nombreComercial")]
        public string TradeName { get; set; } = "";
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

    [XmlRoot(ElementName = "compensacion")]
    public class CreditNoteCompensation
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "tarifa")]
        public string Rate { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "compensaciones")]
    public class CreditNoteCompensations
    {
        [XmlElement(ElementName = "compensacion")]
        public List<CreditNoteCompensation> Compensation { get; set; } = [];
    }

    [XmlRoot(ElementName = "totalImpuesto")]
    public class CreditNoteTotalTax
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string PercentageCode { get; set; } = "";
        [XmlElement(ElementName = "baseImponible")]
        public string TaxableBase { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
        [XmlElement(ElementName = "valorDevolucionIva")]
        public string VatRefundAmount { get; set; } = "";
    }

    [XmlRoot(ElementName = "totalConImpuestos")]
    public class CreditNoteTaxTotals
    {
        [XmlElement(ElementName = "totalImpuesto")]
        public List<CreditNoteTotalTax> TotalTax { get; set; } = [];
    }

    [XmlRoot(ElementName = "infoNotaCredito")]
    public class CreditNoteInformation
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string IssueDate { get; set; } = "";
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string EstablishmentAddress { get; set; } = "";
        [XmlElement(ElementName = "tipoIdentificacionComprador")]
        public string BuyerIdentificationType { get; set; } = "";
        [XmlElement(ElementName = "razonSocialComprador")]
        public string BuyerBusinessName { get; set; } = "";
        [XmlElement(ElementName = "identificacionComprador")]
        public string BuyerIdentification { get; set; } = "";
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string SpecialTaxpayer { get; set; } = "";
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string AccountingRequired { get; set; } = "";
        [XmlElement(ElementName = "rise")]
        public string Rise { get; set; } = "";
        [XmlElement(ElementName = "codDocModificado")]
        public string ModifiedDocumentCode { get; set; } = "";
        [XmlElement(ElementName = "numDocModificado")]
        public string ModifiedDocumentNumber { get; set; } = "";
        [XmlElement(ElementName = "fechaEmisionDocSustento")]
        public string SupportingDocumentIssueDate { get; set; } = "";
        [XmlElement(ElementName = "totalSinImpuestos")]
        public string TotalWithoutTaxes { get; set; } = "";
        [XmlElement(ElementName = "compensaciones")]
        public CreditNoteCompensations? Compensations { get; set; }
        [XmlElement(ElementName = "valorModificacion")]
        public string ModificationAmount { get; set; } = "";
        [XmlElement(ElementName = "moneda")]
        public string Currency { get; set; } = "";
        [XmlElement(ElementName = "totalConImpuestos")]
        public CreditNoteTaxTotals? TaxTotals { get; set; }
        [XmlElement(ElementName = "motivo")]
        public string Reason { get; set; } = "";
    }

    [XmlRoot(ElementName = "detAdicional")]
    public class CreditNoteAdditionalItemField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlAttribute(AttributeName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "detallesAdicionales")]
    public class CreditNoteAdditionalItemFields
    {
        [XmlElement(ElementName = "detAdicional")]
        public List<CreditNoteAdditionalItemField> AdditionalItemField { get; set; } = [];
    }

    [XmlRoot(ElementName = "impuesto")]
    public class CreditNoteTax
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
    public class CreditNoteTaxes
    {
        [XmlElement(ElementName = "impuesto")]
        public List<CreditNoteTax> Tax { get; set; } = [];
    }

    [XmlRoot(ElementName = "detalle")]
    public class CreditNoteItem
    {
        [XmlElement(ElementName = "codigoInterno")]
        public string InternalCode { get; set; } = "";
        [XmlElement(ElementName = "codigoAdicional")]
        public string AdditionalCode { get; set; } = "";
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
        [XmlElement(ElementName = "detallesAdicionales")]
        public CreditNoteAdditionalItemFields? AdditionalItemFields { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public CreditNoteTaxes? Taxes { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class CreditNoteItems
    {
        [XmlElement(ElementName = "detalle")]
        public List<CreditNoteItem> Item { get; set; } = [];
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class CreditNoteFiscalMachine
    {
        [XmlElement(ElementName = "marca")]
        public string Brand { get; set; } = "";
        [XmlElement(ElementName = "modelo")]
        public string Model { get; set; } = "";
        [XmlElement(ElementName = "serie")]
        public string SerialNumber { get; set; } = "";
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CreditNoteAdditionalField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlText]
        public string Text { get; set; } = "";
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class CreditNoteAdditionalInformation
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CreditNoteAdditionalField> AdditionalField { get; set; } = [];
    }

    [XmlRoot(ElementName = "notaCredito")]
    public class CreditNote
    {
        [XmlElement(ElementName = "infoTributaria")]
        public CreditNoteTaxInformation? TaxInformation { get; set; }
        [XmlElement(ElementName = "infoNotaCredito")]
        public CreditNoteInformation? CreditNoteInformation { get; set; }
        [XmlElement(ElementName = "detalles")]
        public CreditNoteItems? Items { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public CreditNoteFiscalMachine? FiscalMachine { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public CreditNoteAdditionalInformation? AdditionalInformation { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; } = "";
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; } = "";
    }

}
