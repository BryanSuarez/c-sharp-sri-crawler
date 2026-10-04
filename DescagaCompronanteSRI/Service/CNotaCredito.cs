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
    public class InfoTributariaNotaCredito
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

    [XmlRoot(ElementName = "compensacion")]
    public class CompensacionNotaCredito
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "tarifa")]
        public string Tarifa { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "compensaciones")]
    public class CompensacionesNotaCredito
    {
        [XmlElement(ElementName = "compensacion")]
        public List<CompensacionNotaCredito> Compensacion { get; set; }
    }

    [XmlRoot(ElementName = "totalImpuesto")]
    public class TotalImpuestoNotaCredito
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "codigoPorcentaje")]
        public string CodigoPorcentaje { get; set; }
        [XmlElement(ElementName = "baseImponible")]
        public string BaseImponible { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
        [XmlElement(ElementName = "valorDevolucionIva")]
        public string ValorDevolucionIva { get; set; }
    }

    [XmlRoot(ElementName = "totalConImpuestos")]
    public class TotalConImpuestosNotaCredito
    {
        [XmlElement(ElementName = "totalImpuesto")]
        public List<TotalImpuestoNotaCredito> TotalImpuesto { get; set; }
    }

    [XmlRoot(ElementName = "infoNotaCredito")]
    public class InfoNotaCreditoNotaCredito
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string FechaEmision { get; set; }
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string DirEstablecimiento { get; set; }
        [XmlElement(ElementName = "tipoIdentificacionComprador")]
        public string TipoIdentificacionComprador { get; set; }
        [XmlElement(ElementName = "razonSocialComprador")]
        public string RazonSocialComprador { get; set; }
        [XmlElement(ElementName = "identificacionComprador")]
        public string IdentificacionComprador { get; set; }
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string ContribuyenteEspecial { get; set; }
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string ObligadoContabilidad { get; set; }
        [XmlElement(ElementName = "rise")]
        public string Rise { get; set; }
        [XmlElement(ElementName = "codDocModificado")]
        public string CodDocModificado { get; set; }
        [XmlElement(ElementName = "numDocModificado")]
        public string NumDocModificado { get; set; }
        [XmlElement(ElementName = "fechaEmisionDocSustento")]
        public string FechaEmisionDocSustento { get; set; }
        [XmlElement(ElementName = "totalSinImpuestos")]
        public string TotalSinImpuestos { get; set; }
        [XmlElement(ElementName = "compensaciones")]
        public CompensacionesNotaCredito Compensaciones { get; set; }
        [XmlElement(ElementName = "valorModificacion")]
        public string ValorModificacion { get; set; }
        [XmlElement(ElementName = "moneda")]
        public string Moneda { get; set; }
        [XmlElement(ElementName = "totalConImpuestos")]
        public TotalConImpuestosNotaCredito TotalConImpuestos { get; set; }
        [XmlElement(ElementName = "motivo")]
        public string Motivo { get; set; }
    }

    [XmlRoot(ElementName = "detAdicional")]
    public class DetAdicionalNotaCredito
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlAttribute(AttributeName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "detallesAdicionales")]
    public class DetallesAdicionalesNotaCredito
    {
        [XmlElement(ElementName = "detAdicional")]
        public List<DetAdicionalNotaCredito> DetAdicional { get; set; }
    }

    [XmlRoot(ElementName = "impuesto")]
    public class ImpuestoNotaCredito
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
    public class ImpuestosNotaCredito
    {
        [XmlElement(ElementName = "impuesto")]
        public List<ImpuestoNotaCredito> Impuesto { get; set; }
    }

    [XmlRoot(ElementName = "detalle")]
    public class DetalleNotaCredito
    {
        [XmlElement(ElementName = "codigoInterno")]
        public string CodigoInterno { get; set; }
        [XmlElement(ElementName = "codigoAdicional")]
        public string CodigoAdicional { get; set; }
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
        [XmlElement(ElementName = "detallesAdicionales")]
        public DetallesAdicionalesNotaCredito DetallesAdicionales { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public ImpuestosNotaCredito Impuestos { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class DetallesNotaCredito
    {
        [XmlElement(ElementName = "detalle")]
        public List<DetalleNotaCredito> Detalle { get; set; }
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class MaquinaFiscalNotaCredito
    {
        [XmlElement(ElementName = "marca")]
        public string Marca { get; set; }
        [XmlElement(ElementName = "modelo")]
        public string Modelo { get; set; }
        [XmlElement(ElementName = "serie")]
        public string Serie { get; set; }
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CampoAdicionalNotaCredito
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InfoAdicionalNotaCredito
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CampoAdicionalNotaCredito> CampoAdicional { get; set; }
    }

    [XmlRoot(ElementName = "notaCredito")]
    public class NotaCredito
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InfoTributariaNotaCredito InfoTributaria { get; set; }
        [XmlElement(ElementName = "infoNotaCredito")]
        public InfoNotaCreditoNotaCredito InfoNotaCredito { get; set; }
        [XmlElement(ElementName = "detalles")]
        public DetallesNotaCredito Detalles { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public MaquinaFiscalNotaCredito MaquinaFiscal { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InfoAdicionalNotaCredito InfoAdicional { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }
    }

}