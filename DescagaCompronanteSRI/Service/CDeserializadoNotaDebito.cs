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
    public class InfoTributariaNotaDebito
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

    [XmlRoot(ElementName = "impuesto")]
    public class ImpuestoNotaDebito
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
        [XmlElement(ElementName = "valorDevolucionIva")]
        public string ValorDevolucionIva { get; set; }
    }

    [XmlRoot(ElementName = "impuestos")]
    public class ImpuestosNotaDebito
    {
        [XmlElement(ElementName = "impuesto")]
        public List<ImpuestoNotaDebito> Impuesto { get; set; }
    }

    [XmlRoot(ElementName = "compensacion")]
    public class CompensacionNotaDebito
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "tarifa")]
        public string Tarifa { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "compensaciones")]
    public class CompensacionesNotaDebito
    {
        [XmlElement(ElementName = "compensacion")]
        public List<CompensacionNotaDebito> Compensacion { get; set; }
    }

    [XmlRoot(ElementName = "pago")]
    public class PagoNotaDebito
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
    public class PagosNotaDebito
    {
        [XmlElement(ElementName = "pago")]
        public List<PagoNotaDebito> Pago { get; set; }
    }

    [XmlRoot(ElementName = "infoNotaDebito")]
    public class InfoNotaDebito
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
        [XmlElement(ElementName = "impuestos")]
        public Impuestos Impuestos { get; set; }
        [XmlElement(ElementName = "compensaciones")]
        public CompensacionesNotaDebito Compensaciones { get; set; }
        [XmlElement(ElementName = "valorTotal")]
        public string ValorTotal { get; set; }
        [XmlElement(ElementName = "pagos")]
        public List<PagosNotaDebito> Pagos { get; set; }
    }

    [XmlRoot(ElementName = "motivo")]
    public class MotivoNotaDebito
    {
        [XmlElement(ElementName = "razon")]
        public string Razon { get; set; }
        [XmlElement(ElementName = "valor")]
        public string Valor { get; set; }
    }

    [XmlRoot(ElementName = "motivos")]
    public class MotivosNotaDebito
    {
        [XmlElement(ElementName = "motivo")]
        public List<MotivoNotaDebito> Motivo { get; set; }
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class MaquinaFiscalNotaDebito
    {
        [XmlElement(ElementName = "marca")]
        public string Marca { get; set; }
        [XmlElement(ElementName = "modelo")]
        public string Modelo { get; set; }
        [XmlElement(ElementName = "serie")]
        public string Serie { get; set; }
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CampoAdicionalNotaDebito
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InfoAdicionalNotaDebito
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CampoAdicional> CampoAdicional { get; set; }
    }

    [XmlRoot(ElementName = "notaDebito")]
    public class NotaDebito
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InfoTributariaNotaDebito InfoTributaria { get; set; }
        [XmlElement(ElementName = "infoNotaDebito")]
        public InfoNotaDebito InfoNotaDebito { get; set; }
        [XmlElement(ElementName = "motivos")]
        public MotivosNotaDebito Motivos { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public MaquinaFiscalNotaDebito MaquinaFiscal { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InfoAdicionalNotaDebito InfoAdicional { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }
    }

}
