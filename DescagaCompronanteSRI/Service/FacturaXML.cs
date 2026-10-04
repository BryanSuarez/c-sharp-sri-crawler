using System;
using System.Xml.Serialization;
using System.Collections.Generic;
namespace DescagaCompronanteSRI.Services
{
    [XmlRoot(ElementName = "infoTributaria")]
    public class InfoTributariaFactura
    {
        [XmlElement(ElementName = "ambiente")]
        public string Ambiente { get; set; }
        [XmlElement(ElementName = "tipoEmision")]
        public string TipoEmision { get; set; }
        [XmlElement(ElementName = "razonSocial")]
        public string RazonSocial { get; set; }
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
    public class TotalImpuesto
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string CodigoPorcentaje { get; set; }
        [XmlElement(ElementName = "baseImponible")]
        public string BaseImponible { get; set; }
        [XmlElement(ElementName = "tarifa")]
        public string Tarifa { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "totalConImpuestos")]
    public class TotalConImpuestos
    {
        [XmlElement(ElementName = "totalImpuesto")]
        public TotalImpuesto TotalImpuesto { get; set; }
    }

    [XmlRoot(ElementName = "pago")]
    public class Pago
    {
        [XmlElement(ElementName = "formaPago")]
        public string FormaPago { get; set; }
        [XmlElement(ElementName = "total")]
        public string Total { get; set; }
    }

    [XmlRoot(ElementName = "pagos")]
    public class Pagos
    {
        [XmlElement(ElementName = "pago")]
        public Pago Pago { get; set; }
    }

    [XmlRoot(ElementName = "infoFactura")]
    public class InfoFactura
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string FechaEmision { get; set; }
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string DirEstablecimiento { get; set; }
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string ContribuyenteEspecial { get; set; }
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string ObligadoContabilidad { get; set; }
        [XmlElement(ElementName = "tipoIdentificacionComprador")]
        public string TipoIdentificacionComprador { get; set; }
        [XmlElement(ElementName = "razonSocialComprador")]
        public string RazonSocialComprador { get; set; }
        [XmlElement(ElementName = "identificacionComprador")]
        public string IdentificacionComprador { get; set; }
        [XmlElement(ElementName = "direccionComprador")]
        public string DireccionComprador { get; set; }
        [XmlElement(ElementName = "totalSinImpuestos")]
        public string TotalSinImpuestos { get; set; }
        [XmlElement(ElementName = "totalDescuento")]
        public string TotalDescuento { get; set; }
        [XmlElement(ElementName = "totalConImpuestos")]
        public TotalConImpuestos TotalConImpuestos { get; set; }
        [XmlElement(ElementName = "propina")]
        public string Propina { get; set; }
        [XmlElement(ElementName = "importeTotal")]
        public string ImporteTotal { get; set; }
        [XmlElement(ElementName = "moneda")]
        public string Moneda { get; set; }
        [XmlElement(ElementName = "pagos")]
        public Pagos Pagos { get; set; }
    }

    [XmlRoot(ElementName = "impuesto")]
    public class Impuesto
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
    public class Impuestos
    {
        [XmlElement(ElementName = "impuesto")]
        public List<Impuesto> Impuesto { get; set; }
    }

    [XmlRoot(ElementName = "detalle")]
    public class DetalleFactura
    {
        [XmlElement(ElementName = "codigoPrincipal")]
        public string CodigoPrincipal { get; set; }
        [XmlElement(ElementName = "codigoAuxiliar")]
        public string CodigoAuxiliar { get; set; }
        [XmlElement(ElementName = "descripcion")]
        public string Descripcion { get; set; }
        [XmlElement(ElementName = "cantidad")]
        public string Cantidad { get; set; }
        [XmlElement(ElementName = "precioUnitario")]
        public string PrecioUnitario { get; set; }
        [XmlElement(ElementName = "descuento")]
        public string Descuento { get; set; }
        [XmlElement(ElementName = "precioTotalSinImpuesto")]
        public string PrecioTotalSinImpuesto { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public Impuestos Impuestos { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class DetallesFactura
    {
        [XmlElement(ElementName = "detalle")]
        public List<DetalleFactura> DetalleFacturas { get; set; }
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CampoAdicional
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InfoAdicional
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CampoAdicional> CampoAdicional { get; set; }
    }

    [XmlRoot(ElementName = "factura")]
    public class FacturaXML
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InfoTributariaFactura InfoTributariaFactura { get; set; }
        [XmlElement(ElementName = "infoFactura")]
        public InfoFactura InfoFactura { get; set; }
        [XmlElement(ElementName = "detalles")]
        public DetallesFactura DetallesFactura { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InfoAdicional InfoAdicional { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }
    }

}
