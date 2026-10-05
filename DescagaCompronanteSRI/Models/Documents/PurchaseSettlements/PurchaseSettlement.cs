/*
 Licensed under the Apache License, Version 2.0

 http://www.apache.org/licenses/LICENSE-2.0
 */
using System;
using System.Xml.Serialization;
using System.Collections.Generic;
namespace DescagaCompronanteSRI.Models.Documents.PurchaseSettlements
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class PurchaseSettlementTaxInformation
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

    [XmlRoot(ElementName = "totalImpuesto")]
    public class PurchaseSettlementTotalTax
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string PercentageCode { get; set; } = "";
        [XmlElement(ElementName = "descuentoAdicional")]
        public string AdditionalDiscount { get; set; } = "";
        [XmlElement(ElementName = "baseImponible")]
        public string TaxableBase { get; set; } = "";
        [XmlElement(ElementName = "tarifa")]
        public string Rate { get; set; } = "";
        [XmlElement(ElementName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "totalConImpuestos")]
    public class PurchaseSettlementTaxTotals
    {
        [XmlElement(ElementName = "totalImpuesto")]
        public List<PurchaseSettlementTotalTax> TotalTax { get; set; } = [];
    }

    [XmlRoot(ElementName = "pago")]
    public class PurchaseSettlementPayment
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
    public class PurchaseSettlementPayments
    {
        [XmlElement(ElementName = "pago")]
        public List<PurchaseSettlementPayment> Payment { get; set; } = [];
    }

    [XmlRoot(ElementName = "infoLiquidacionCompra")]
    public class PurchaseSettlementInformation
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string IssueDate { get; set; } = "";
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string EstablishmentAddress { get; set; } = "";
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string SpecialTaxpayer { get; set; } = "";
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string AccountingRequired { get; set; } = "";
        [XmlElement(ElementName = "tipoIdentificacionProveedor")]
        public string SupplierIdentificationType { get; set; } = "";
        [XmlElement(ElementName = "razonSocialProveedor")]
        public string SupplierBusinessName { get; set; } = "";
        [XmlElement(ElementName = "identificacionProveedor")]
        public string SupplierIdentification { get; set; } = "";
        [XmlElement(ElementName = "direccionProveedor")]
        public string SupplierAddress { get; set; } = "";
        [XmlElement(ElementName = "totalSinImpuestos")]
        public string TotalWithoutTaxes { get; set; } = "";
        [XmlElement(ElementName = "totalDescuento")]
        public string TotalDiscount { get; set; } = "";
        [XmlElement(ElementName = "codDocReembolso")]
        public string ReimbursementDocumentCode { get; set; } = "";
        [XmlElement(ElementName = "totalComprobantesReembolso")]
        public string TotalReimbursementDocuments { get; set; } = "";
        [XmlElement(ElementName = "totalBaseImponibleReembolso")]
        public string TotalReimbursementTaxableBase { get; set; } = "";
        [XmlElement(ElementName = "totalImpuestoReembolso")]
        public string TotalReimbursementTax { get; set; } = "";
        [XmlElement(ElementName = "totalConImpuestos")]
        public PurchaseSettlementTaxTotals? TaxTotals { get; set; }
        [XmlElement(ElementName = "importeTotal")]
        public string TotalAmount { get; set; } = "";
        [XmlElement(ElementName = "moneda")]
        public string Currency { get; set; } = "";
        [XmlElement(ElementName = "pagos")]
        public PurchaseSettlementPayments? Payments { get; set; }
    }

    [XmlRoot(ElementName = "detAdicional")]
    public class PurchaseSettlementAdditionalItemField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlAttribute(AttributeName = "valor")]
        public string Value { get; set; } = "";
    }

    [XmlRoot(ElementName = "detallesAdicionales")]
    public class PurchaseSettlementAdditionalItemFields
    {
        [XmlElement(ElementName = "detAdicional")]
        public List<PurchaseSettlementAdditionalItemField> AdditionalItemField { get; set; } = [];
    }

    [XmlRoot(ElementName = "impuesto")]
    public class PurchaseSettlementTax
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
    public class PurchaseSettlementTaxes
    {
        [XmlElement(ElementName = "impuesto")]
        public List<PurchaseSettlementTax> Tax { get; set; } = [];
    }

    [XmlRoot(ElementName = "detalle")]
    public class PurchaseSettlementItem
    {
        [XmlElement(ElementName = "codigoPrincipal")]
        public string PrimaryCode { get; set; } = "";
        [XmlElement(ElementName = "codigoAuxiliar")]
        public string AuxiliaryCode { get; set; } = "";
        [XmlElement(ElementName = "descripcion")]
        public string Description { get; set; } = "";
        [XmlElement(ElementName = "unidadMedida")]
        public string MeasurementUnit { get; set; } = "";
        [XmlElement(ElementName = "cantidad")]
        public string Quantity { get; set; } = "";
        [XmlElement(ElementName = "precioUnitario")]
        public string UnitPrice { get; set; } = "";
        [XmlElement(ElementName = "precioSinSubsidio")]
        public string PriceWithoutSubsidy { get; set; } = "";
        [XmlElement(ElementName = "descuento")]
        public string Discount { get; set; } = "";
        [XmlElement(ElementName = "precioTotalSinImpuesto")]
        public string TotalPriceWithoutTax { get; set; } = "";
        [XmlElement(ElementName = "detallesAdicionales")]
        public PurchaseSettlementAdditionalItemFields? AdditionalItemFields { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public PurchaseSettlementTaxes? Taxes { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class PurchaseSettlementItems
    {
        [XmlElement(ElementName = "detalle")]
        public List<PurchaseSettlementItem> Item { get; set; } = [];
    }

    [XmlRoot(ElementName = "detalleImpuesto")]
    public class PurchaseSettlementItemTax
    {
        [XmlElement(ElementName = "codigo")]
        public string Code { get; set; } = "";
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string PercentageCode { get; set; } = "";
        [XmlElement(ElementName = "tarifa")]
        public string Rate { get; set; } = "";
        [XmlElement(ElementName = "baseImponibleReembolso")]
        public string ReimbursementTaxableBase { get; set; } = "";
        [XmlElement(ElementName = "impuestoReembolso")]
        public string ReimbursementTax { get; set; } = "";
    }

    [XmlRoot(ElementName = "detalleImpuestos")]
    public class PurchaseSettlementItemTaxes
    {
        [XmlElement(ElementName = "detalleImpuesto")]
        public List<PurchaseSettlementItemTax> ItemTax { get; set; } = [];
    }

    [XmlRoot(ElementName = "reembolsoDetalle")]
    public class PurchaseSettlementReimbursementItem
    {
        [XmlElement(ElementName = "tipoIdentificacionProveedorReembolso")]
        public string ReimbursementSupplierIdentificationType { get; set; } = "";
        [XmlElement(ElementName = "identificacionProveedorReembolso")]
        public string ReimbursementSupplierIdentification { get; set; } = "";
        [XmlElement(ElementName = "codPaisPagoProveedorReembolso")]
        public string ReimbursementSupplierPaymentCountryCode { get; set; } = "";
        [XmlElement(ElementName = "tipoProveedorReembolso")]
        public string ReimbursementSupplierType { get; set; } = "";
        [XmlElement(ElementName = "codDocReembolso")]
        public string ReimbursementDocumentCode { get; set; } = "";
        [XmlElement(ElementName = "estabDocReembolso")]
        public string ReimbursementDocumentEstablishment { get; set; } = "";
        [XmlElement(ElementName = "ptoEmiDocReembolso")]
        public string ReimbursementDocumentIssuePoint { get; set; } = "";
        [XmlElement(ElementName = "secuencialDocReembolso")]
        public string ReimbursementDocumentSequence { get; set; } = "";
        [XmlElement(ElementName = "fechaEmisionDocReembolso")]
        public string ReimbursementDocumentIssueDate { get; set; } = "";
        [XmlElement(ElementName = "numeroautorizacionDocReemb")]
        public string ReimbursementDocumentAuthorizationNumber { get; set; } = "";
        [XmlElement(ElementName = "detalleImpuestos")]
        public PurchaseSettlementItemTaxes? ItemTaxes { get; set; }
    }

    [XmlRoot(ElementName = "reembolsos")]
    public class PurchaseSettlementReimbursements
    {
        [XmlElement(ElementName = "reembolsoDetalle")]
        public List<PurchaseSettlementReimbursementItem> ReimbursementItem { get; set; } = [];
    }

    [XmlRoot(ElementName = "tipoNegociable")]
    public class PurchaseSettlementNegotiableInformation
    {
        [XmlElement(ElementName = "correo")]
        public string Email { get; set; } = "";
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class PurchaseSettlementFiscalMachine
    {
        [XmlElement(ElementName = "marca")]
        public string Brand { get; set; } = "";
        [XmlElement(ElementName = "modelo")]
        public string Model { get; set; } = "";
        [XmlElement(ElementName = "serie")]
        public string SerialNumber { get; set; } = "";
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class PurchaseSettlementAdditionalField
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Name { get; set; } = "";
        [XmlText]
        public string Text { get; set; } = "";
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class PurchaseSettlementAdditionalInformation
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<PurchaseSettlementAdditionalField> AdditionalField { get; set; } = [];
    }

    [XmlRoot(ElementName = "liquidacionCompra")]
    public class PurchaseSettlement
    {
        [XmlElement(ElementName = "infoTributaria")]
        public PurchaseSettlementTaxInformation? TaxInformation { get; set; }
        [XmlElement(ElementName = "infoLiquidacionCompra")]
        public PurchaseSettlementInformation? PurchaseSettlementInformation { get; set; }
        [XmlElement(ElementName = "detalles")]
        public PurchaseSettlementItems? Items { get; set; }
        [XmlElement(ElementName = "reembolsos")]
        public PurchaseSettlementReimbursements? Reimbursements { get; set; }
        [XmlElement(ElementName = "tipoNegociable")]
        public PurchaseSettlementNegotiableInformation? NegotiableInformation { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public PurchaseSettlementFiscalMachine? FiscalMachine { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public PurchaseSettlementAdditionalInformation? AdditionalInformation { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; } = "";
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; } = "";
    }

}
