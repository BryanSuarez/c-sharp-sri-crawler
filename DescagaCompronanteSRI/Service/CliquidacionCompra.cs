/* 
 Licensed under the Apache License, Version 2.0

 http://www.apache.org/licenses/LICENSE-2.0
 */
using System;
using System.Xml.Serialization;
using System.Collections.Generic;
namespace DescagaCompronanteSRI.Services
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class InfoTributariaLiquidacionCompra
    {
        [XmlElement(ElementName = "ambiente")]
        public string Ambiente { get; set; }
        [XmlElement(ElementName = "tipoEmision")]
        public string TipoEmision { get; set; }
        [XmlElement(ElementName = "razonSocial")]
        public string RazonSocial { get; set; }
        [XmlElement(ElementName = "nombreComercial")]
        public string NombreComercial { get; set; }
        [XmlElement(ElementName = "ruc")]
        public string Ruc { get; set; }
        [XmlElement(ElementName = "claveAcceso")]
        public string ClaveAcceso { get; set; }
        [XmlElement(ElementName = "codDoc")]
        public string CodDoc { get; set; }
        [XmlElement(ElementName = "estab")]
        public string Estab { get; set; }
        [XmlElement(ElementName = "ptoEmi")]
        public string PtoEmi { get; set; }
        [XmlElement(ElementName = "secuencial")]
        public string Secuencial { get; set; }
        [XmlElement(ElementName = "dirMatriz")]
        public string DirMatriz { get; set; }
        [XmlElement(ElementName = "agenteRetencion")]
        public string AgenteRetencion { get; set; }
        [XmlElement(ElementName = "contribuyenteRimpe")]
        public string ContribuyenteRimpe { get; set; }
    }

    [XmlRoot(ElementName = "totalImpuesto")]
    public class TotalImpuestoLiquidacionCompra
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string CodigoPorcentaje { get; set; }
        [XmlElement(ElementName = "descuentoAdicional")]
        public string DescuentoAdicional { get; set; }
        [XmlElement(ElementName = "baseImponible")]
        public string BaseImponible { get; set; }
        [XmlElement(ElementName = "tarifa")]
        public string Tarifa { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "totalConImpuestos")]
    public class TotalConImpuestosLiquidacionCompra
    {
        [XmlElement(ElementName = "totalImpuesto")]
        public List<TotalImpuestoLiquidacionCompra> TotalImpuesto { get; set; }
    }

    [XmlRoot(ElementName = "pago")]
    public class PagoLiquidacionCompra
    {
        [XmlElement(ElementName = "formaPago")]
        public string FormaPago { get; set; }
        [XmlElement(ElementName = "total")]
        public string Total { get; set; }
        [XmlElement(ElementName = "plazo")]
        public string Plazo { get; set; }
        [XmlElement(ElementName = "unidadTiempo")]
        public string UnidadTiempo { get; set; }
    }

    [XmlRoot(ElementName = "pagos")]
    public class PagosLiquidacionCompra
    {
        [XmlElement(ElementName = "pago")]
        public List<PagoLiquidacionCompra> Pago { get; set; }
    }

    [XmlRoot(ElementName = "infoLiquidacionCompra")]
    public class InfoLiquidacionCompra
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string FechaEmision { get; set; }
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string DirEstablecimiento { get; set; }
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string ContribuyenteEspecial { get; set; }
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string ObligadoContabilidad { get; set; }
        [XmlElement(ElementName = "tipoIdentificacionProveedor")]
        public string TipoIdentificacionProveedor { get; set; }
        [XmlElement(ElementName = "razonSocialProveedor")]
        public string RazonSocialProveedor { get; set; }
        [XmlElement(ElementName = "identificacionProveedor")]
        public string IdentificacionProveedor { get; set; }
        [XmlElement(ElementName = "direccionProveedor")]
        public string DireccionProveedor { get; set; }
        [XmlElement(ElementName = "totalSinImpuestos")]
        public string TotalSinImpuestos { get; set; }
        [XmlElement(ElementName = "totalDescuento")]
        public string TotalDescuento { get; set; }
        [XmlElement(ElementName = "codDocReembolso")]
        public string CodDocReembolso { get; set; }
        [XmlElement(ElementName = "totalComprobantesReembolso")]
        public string TotalComprobantesReembolso { get; set; }
        [XmlElement(ElementName = "totalBaseImponibleReembolso")]
        public string TotalBaseImponibleReembolso { get; set; }
        [XmlElement(ElementName = "totalImpuestoReembolso")]
        public string TotalImpuestoReembolso { get; set; }
        [XmlElement(ElementName = "totalConImpuestos")]
        public TotalConImpuestosLiquidacionCompra TotalConImpuestos { get; set; }
        [XmlElement(ElementName = "importeTotal")]
        public string ImporteTotal { get; set; }
        [XmlElement(ElementName = "moneda")]
        public string Moneda { get; set; }
        [XmlElement(ElementName = "pagos")]
        public PagosLiquidacionCompra Pagos { get; set; }
    }

    [XmlRoot(ElementName = "detAdicional")]
    public class DetAdicionalLiquidacionCompra
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlAttribute(AttributeName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "detallesAdicionales")]
    public class DetallesAdicionalesLiquidacionCompra
    {
        [XmlElement(ElementName = "detAdicional")]
        public List<DetAdicionalLiquidacionCompra> DetAdicional { get; set; }
    }

    [XmlRoot(ElementName = "impuesto")]
    public class ImpuestoLiquidacionCompra
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string CodigoPorcentaje { get; set; }
        [XmlElement(ElementName = "tarifa")]
        public string Tarifa { get; set; }
        [XmlElement(ElementName = "baseImponible")]
        public string BaseImponible { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "impuestos")]
    public class ImpuestosLiquidacionCompra
    {
        [XmlElement(ElementName = "impuesto")]
        public List<ImpuestoLiquidacionCompra> Impuesto { get; set; }
    }

    [XmlRoot(ElementName = "detalle")]
    public class DetalleLiquidacionCompra
    {
        [XmlElement(ElementName = "codigoPrincipal")]
        public string CodigoPrincipal { get; set; }
        [XmlElement(ElementName = "codigoAuxiliar")]
        public string CodigoAuxiliar { get; set; }
        [XmlElement(ElementName = "descripcion")]
        public string Descripcion { get; set; }
        [XmlElement(ElementName = "unidadMedida")]
        public string UnidadMedida { get; set; }
        [XmlElement(ElementName = "cantidad")]
        public string Cantidad { get; set; }
        [XmlElement(ElementName = "precioUnitario")]
        public string PrecioUnitario { get; set; }
        [XmlElement(ElementName = "precioSinSubsidio")]
        public string PrecioSinSubsidio { get; set; }
        [XmlElement(ElementName = "descuento")]
        public string Descuento { get; set; }
        [XmlElement(ElementName = "precioTotalSinImpuesto")]
        public string PrecioTotalSinImpuesto { get; set; }
        [XmlElement(ElementName = "detallesAdicionales")]
        public DetallesAdicionalesLiquidacionCompra DetallesAdicionales { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public ImpuestosLiquidacionCompra Impuestos { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class DetallesLiquidacionCompra
    {
        [XmlElement(ElementName = "detalle")]
        public List<DetalleLiquidacionCompra> Detalle { get; set; }
    }

    [XmlRoot(ElementName = "detalleImpuesto")]
    public class DetalleImpuestoLiquidacionCompra
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string CodigoPorcentaje { get; set; }
        [XmlElement(ElementName = "tarifa")]
        public string Tarifa { get; set; }
        [XmlElement(ElementName = "baseImponibleReembolso")]
        public string BaseImponibleReembolso { get; set; }
        [XmlElement(ElementName = "impuestoReembolso")]
        public string ImpuestoReembolso { get; set; }
    }

    [XmlRoot(ElementName = "detalleImpuestos")]
    public class DetalleImpuestosLiquidacionCompra
    {
        [XmlElement(ElementName = "detalleImpuesto")]
        public List<DetalleImpuestoLiquidacionCompra> DetalleImpuesto { get; set; }
    }

    [XmlRoot(ElementName = "reembolsoDetalle")]
    public class ReembolsoDetalleLiquidacionCompra
    {
        [XmlElement(ElementName = "tipoIdentificacionProveedorReembolso")]
        public string TipoIdentificacionProveedorReembolso { get; set; }
        [XmlElement(ElementName = "identificacionProveedorReembolso")]
        public string IdentificacionProveedorReembolso { get; set; }
        [XmlElement(ElementName = "codPaisPagoProveedorReembolso")]
        public string CodPaisPagoProveedorReembolso { get; set; }
        [XmlElement(ElementName = "tipoProveedorReembolso")]
        public string TipoProveedorReembolso { get; set; }
        [XmlElement(ElementName = "codDocReembolso")]
        public string CodDocReembolso { get; set; }
        [XmlElement(ElementName = "estabDocReembolso")]
        public string EstabDocReembolso { get; set; }
        [XmlElement(ElementName = "ptoEmiDocReembolso")]
        public string PtoEmiDocReembolso { get; set; }
        [XmlElement(ElementName = "secuencialDocReembolso")]
        public string SecuencialDocReembolso { get; set; }
        [XmlElement(ElementName = "fechaEmisionDocReembolso")]
        public string FechaEmisionDocReembolso { get; set; }
        [XmlElement(ElementName = "numeroautorizacionDocReemb")]
        public string NumeroautorizacionDocReemb { get; set; }
        [XmlElement(ElementName = "detalleImpuestos")]
        public DetalleImpuestosLiquidacionCompra DetalleImpuestos { get; set; }
    }

    [XmlRoot(ElementName = "reembolsos")]
    public class ReembolsosLiquidacionCompra
    {
        [XmlElement(ElementName = "reembolsoDetalle")]
        public List<ReembolsoDetalleLiquidacionCompra> ReembolsoDetalle { get; set; }
    }

    [XmlRoot(ElementName = "tipoNegociable")]
    public class TipoNegociableLiquidacionCompra
    {
        [XmlElement(ElementName = "correo")]
        public string Correo { get; set; }
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class MaquinaFiscalLiquidacionCompra
    {
        [XmlElement(ElementName = "marca")]
        public string Marca { get; set; }
        [XmlElement(ElementName = "modelo")]
        public string Modelo { get; set; }
        [XmlElement(ElementName = "serie")]
        public string Serie { get; set; }
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CampoAdicionalLiquidacionCompra
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InfoAdicionalLiquidacionCompra
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CampoAdicionalLiquidacionCompra> CampoAdicional { get; set; }
    }

    [XmlRoot(ElementName = "liquidacionCompra")]
    public class LiquidacionCompra
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InfoTributariaLiquidacionCompra InfoTributaria { get; set; }
        [XmlElement(ElementName = "infoLiquidacionCompra")]
        public InfoLiquidacionCompra InfoLiquidacionCompra { get; set; }
        [XmlElement(ElementName = "detalles")]
        public DetallesLiquidacionCompra Detalles { get; set; }
        [XmlElement(ElementName = "reembolsos")]
        public ReembolsosLiquidacionCompra Reembolsos { get; set; }
        [XmlElement(ElementName = "tipoNegociable")]
        public TipoNegociableLiquidacionCompra TipoNegociable { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public MaquinaFiscalLiquidacionCompra MaquinaFiscal { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InfoAdicionalLiquidacionCompra InfoAdicional { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }
    }

}
