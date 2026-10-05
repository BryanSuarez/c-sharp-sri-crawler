/*
 Licensed under the Apache License, Version 2.0

 http://www.apache.org/licenses/LICENSE-2.0
 */
using System;
using System.Xml.Serialization;
using DescagaCompronanteSRI.Models.Documents.Invoices;
using System.Collections.Generic;

namespace DescagaCompronanteSRI.Models.Documents.RemissionGuides
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class RemissionGuideTaxInformation
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

    [XmlRoot(ElementName = "infoGuiaRemision")]
    public class RemissionGuideInformation
    {
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string EstablishmentAddress { get; set; } = "";
        [XmlElement(ElementName = "dirPartida")]
        public string DepartureAddress { get; set; } = "";
        [XmlElement(ElementName = "razonSocialTransportista")]
        public string CarrierBusinessName { get; set; } = "";
        [XmlElement(ElementName = "tipoIdentificacionTransportista")]
        public string CarrierIdentificationType { get; set; } = "";
        [XmlElement(ElementName = "rucTransportista")]
        public string CarrierTaxpayerId { get; set; } = "";
        [XmlElement(ElementName = "rise")]
        public string Rise { get; set; } = "";
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string AccountingRequired { get; set; } = "";
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string SpecialTaxpayer { get; set; } = "";
        [XmlElement(ElementName = "fechaIniTransporte")]
        public string TransportStartDate { get; set; } = "";
        [XmlElement(ElementName = "fechaFinTransporte")]
        public string TransportEndDate { get; set; } = "";
        [XmlElement(ElementName = "placa")]
        public string LicensePlate { get; set; } = "";
    }

    [XmlRoot(ElementName = "detalle")]
    public class RemissionGuideItem
    {
        [XmlElement(ElementName = "codigoInterno")]
        public string InternalCode { get; set; } = "";
        [XmlElement(ElementName = "codigoAdicional")]
        public string AdditionalCode { get; set; } = "";
        [XmlElement(ElementName = "descripcion")]
        public string Description { get; set; } = "";
        [XmlElement(ElementName = "cantidad")]
        public string Quantity { get; set; } = "";
        [XmlElement(ElementName = "detallesAdicionales")]
        public string AdditionalItemFields { get; set; } = "";
    }

    [XmlRoot(ElementName = "detalles")]
    public class RemissionGuideItems
    {
        [XmlElement(ElementName = "detalle")]
        public List<RemissionGuideItem> Item { get; set; } = [];
    }

    [XmlRoot(ElementName = "destinatario")]
    public class RemissionGuideRecipient
    {
        [XmlElement(ElementName = "identificacionDestinatario")]
        public string RecipientIdentification { get; set; } = "";
        [XmlElement(ElementName = "razonSocialDestinatario")]
        public string RecipientBusinessName { get; set; } = "";
        [XmlElement(ElementName = "dirDestinatario")]
        public string RecipientAddress { get; set; } = "";
        [XmlElement(ElementName = "motivoTraslado")]
        public string TransferReason { get; set; } = "";
        [XmlElement(ElementName = "docAduaneroUnico")]
        public string CustomsDocument { get; set; } = "";
        [XmlElement(ElementName = "codEstabDestino")]
        public string DestinationEstablishmentCode { get; set; } = "";
        [XmlElement(ElementName = "ruta")]
        public string Route { get; set; } = "";
        [XmlElement(ElementName = "codDocSustento")]
        public string SupportingDocumentCode { get; set; } = "";
        [XmlElement(ElementName = "numDocSustento")]
        public string SupportingDocumentNumber { get; set; } = "";
        [XmlElement(ElementName = "numAutDocSustento")]
        public string SupportingDocumentAuthorizationNumber { get; set; } = "";
        [XmlElement(ElementName = "fechaEmisionDocSustento")]
        public string SupportingDocumentIssueDate { get; set; } = "";
        [XmlElement(ElementName = "detalles")]
        public RemissionGuideItems? Items { get; set; }
    }

    [XmlRoot(ElementName = "destinatarios")]
    public class RemissionGuideRecipients
    {
        [XmlElement(ElementName = "destinatario")]
        public List<RemissionGuideRecipient> Recipient { get; set; } = [];
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class RemissionGuideFiscalMachine
    {
        [XmlElement(ElementName = "marca")]
        public string Brand { get; set; } = "";
        [XmlElement(ElementName = "modelo")]
        public string Model { get; set; } = "";
        [XmlElement(ElementName = "serie")]
        public string SerialNumber { get; set; } = "";
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class RemissionGuideAdditionalField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlText]
        public string Text { get; set; } = "";
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class RemissionGuideAdditionalInformation
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<RemissionGuideAdditionalField> AdditionalField { get; set; } = [];
    }

    [XmlRoot(ElementName = "guiaRemision")]
    public class RemissionGuide
    {
        [XmlElement(ElementName = "infoTributaria")]
        public RemissionGuideTaxInformation? TaxInformation { get; set; }
        [XmlElement(ElementName = "infoGuiaRemision")]
        public RemissionGuideInformation? RemissionGuideInformation { get; set; }
        [XmlElement(ElementName = "destinatarios")]
        public RemissionGuideRecipients? Recipients { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public RemissionGuideFiscalMachine? FiscalMachine { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InvoiceAdditionalInformation? AdditionalInformation { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; } = "";
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; } = "";
    }

}
