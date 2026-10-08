/*
 Licensed under the Apache License, Version 2.0

 http://www.apache.org/licenses/LICENSE-2.0
 */
using System;
using System.Xml.Serialization;
using System.Collections.Generic;
namespace DescagaCompronanteSRI.Models.Documents.Withholdings
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class WithholdingTaxInformation
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
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string AccountingRequired { get; set; } = "";

    }

    [XmlRoot(ElementName = "infoCompRetencion")]
    public class WithholdingInformation
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string IssueDate { get; set; } = "";
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string EstablishmentAddress { get; set; } = "";
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string SpecialTaxpayer { get; set; } = "";
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string AccountingRequired { get; set; } = "";
        [XmlElement(ElementName = "tipoIdentificacionSujetoRetenido")]
        public string WithheldPartyIdentificationType { get; set; } = "";
        [XmlElement(ElementName = "razonSocialSujetoRetenido")]
        public string WithheldPartyBusinessName { get; set; } = "";
        [XmlElement(ElementName = "identificacionSujetoRetenido")]
        public string WithheldPartyIdentification { get; set; } = "";
        [XmlElement(ElementName = "periodoFiscal")]
        public string TaxPeriod { get; set; } = "";
    }

    [XmlRoot(ElementName = "impuesto")]
    public class WithholdingTax
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "codigoRetencion")]
        public string WithholdingCode { get; set; } = "";
        [XmlElement(ElementName = "baseImponible")]
        public string TaxableBase { get; set; } = "";
        [XmlElement(ElementName = "porcentajeRetener")]
        public string WithholdingPercentage { get; set; } = "";
        [XmlElement(ElementName = "valorRetenido")]
        public string WithheldAmount { get; set; } = "";
        [XmlElement(ElementName = "codDocSustento")]
        public string SupportingDocumentCode { get; set; } = "";
        [XmlElement(ElementName = "numDocSustento")]
        public string SupportingDocumentNumber { get; set; } = "";
        [XmlElement(ElementName = "fechaEmisionDocSustento")]
        public string SupportingDocumentIssueDate { get; set; } = "";
    }

    [XmlRoot(ElementName = "impuestos")]
    public class WithholdingTaxes
    {
        [XmlElement(ElementName = "impuesto")]
        public List<WithholdingTax> Tax { get; set; } = [];
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class WithholdingFiscalMachine
    {
        [XmlElement(ElementName = "marca")]
        public string Brand { get; set; } = "";
        [XmlElement(ElementName = "modelo")]
        public string Model { get; set; } = "";
        [XmlElement(ElementName = "serie")]
        public string SerialNumber { get; set; } = "";
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class WithholdingAdditionalField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlText]
        public string Text { get; set; } = "";
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class WithholdingAdditionalInformation
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<WithholdingAdditionalField> AdditionalField { get; set; } = [];
    }

    [XmlRoot(ElementName = "comprobanteRetencion")]
    public class WithholdingDocument
    {
        [XmlElement(ElementName = "infoTributaria")]
        public WithholdingTaxInformation? TaxInformation { get; set; }
        [XmlElement(ElementName = "infoCompRetencion")]
        public WithholdingInformation? WithholdingInformation { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public WithholdingTaxes? Taxes { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public WithholdingFiscalMachine? FiscalMachine { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public WithholdingAdditionalInformation? AdditionalInformation { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; } = "";
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; } = "";
    }

}
