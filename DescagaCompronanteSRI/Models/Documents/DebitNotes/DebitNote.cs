/*
 Licensed under the Apache License, Version 2.0

 http://www.apache.org/licenses/LICENSE-2.0
 */
using System;
using System.Xml.Serialization;
using DescagaCompronanteSRI.Models.Documents.Invoices;
using System.Collections.Generic;

namespace DescagaCompronanteSRI.Models.Documents.DebitNotes
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class DebitNoteTaxInformation
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

    [XmlRoot(ElementName = "impuesto")]
    public class DebitNoteTax
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
        [XmlElement(ElementName = "valorDevolucionIva")]
        public string VatRefundAmount { get; set; } = "";
    }

    [XmlRoot(ElementName = "impuestos")]
    public class DebitNoteTaxes
    {
        [XmlElement(ElementName = "impuesto")]
        public List<DebitNoteTax> Tax { get; set; } = [];
    }

    [XmlRoot(ElementName = "compensacion")]
    public class DebitNoteCompensation
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "tarifa")]
        public string Rate { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "compensaciones")]
    public class DebitNoteCompensations
    {
        [XmlElement(ElementName = "compensacion")]
        public List<DebitNoteCompensation> Compensation { get; set; } = [];
    }

    [XmlRoot(ElementName = "pago")]
    public class DebitNotePayment
    {
        [XmlElement(ElementName = "formaPago")]
        public string PaymentMethod { get; set; } = "";
        [XmlElement(ElementName = "total")]
        public string Total { get; set; } = "";
        [XmlElement(ElementName = "plazo")]
        public string Term { get; set; } = "";
        [XmlElement(ElementName = "unidadTiempo")]
        public string TimeUnit { get; set; } = "";
    }

    [XmlRoot(ElementName = "pagos")]
    public class DebitNotePayments
    {
        [XmlElement(ElementName = "pago")]
        public List<DebitNotePayment> Payment { get; set; } = [];
    }

    [XmlRoot(ElementName = "infoNotaDebito")]
    public class DebitNoteInformation
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
        [XmlElement(ElementName = "impuestos")]
        public InvoiceTaxes? Taxes { get; set; }
        [XmlElement(ElementName = "compensaciones")]
        public DebitNoteCompensations? Compensations { get; set; }
        [XmlElement(ElementName = "valorTotal")]
        public string TotalValue { get; set; } = "";
        [XmlElement(ElementName = "pagos")]
        public List<DebitNotePayments> Payments { get; set; } = [];
    }

    [XmlRoot(ElementName = "motivo")]
    public class DebitNoteReason
    {
        [XmlElement(ElementName = "razon")]
        public string Description { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "motivos")]
    public class DebitNoteReasons
    {
        [XmlElement(ElementName = "motivo")]
        public List<DebitNoteReason> Reason { get; set; } = [];
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class DebitNoteFiscalMachine
    {
        [XmlElement(ElementName = "marca")]
        public string Brand { get; set; } = "";
        [XmlElement(ElementName = "modelo")]
        public string Model { get; set; } = "";
        [XmlElement(ElementName = "serie")]
        public string SerialNumber { get; set; } = "";
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class DebitNoteAdditionalField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlText]
        public string Text { get; set; } = "";
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class DebitNoteAdditionalInformation
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<InvoiceAdditionalField> AdditionalField { get; set; } = [];
    }

    [XmlRoot(ElementName = "notaDebito")]
    public class DebitNote
    {
        [XmlElement(ElementName = "infoTributaria")]
        public DebitNoteTaxInformation? TaxInformation { get; set; }
        [XmlElement(ElementName = "infoNotaDebito")]
        public DebitNoteInformation? DebitNoteInformation { get; set; }
        [XmlElement(ElementName = "motivos")]
        public DebitNoteReasons? Reasons { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public DebitNoteFiscalMachine? FiscalMachine { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public DebitNoteAdditionalInformation? AdditionalInformation { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; } = "";
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; } = "";
    }

}
