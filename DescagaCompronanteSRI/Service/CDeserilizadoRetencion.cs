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
    public class InfoTributariaRetencion
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
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string ObligadoContabilidad { get; set; }

    }

    [XmlRoot(ElementName = "infoCompRetencion")]
    public class InfoCompRetencion
    {
        [XmlElement(ElementName = "fechaEmision")]
        public string FechaEmision { get; set; }
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string DirEstablecimiento { get; set; }
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string ContribuyenteEspecial { get; set; }
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string ObligadoContabilidad { get; set; }
        [XmlElement(ElementName = "tipoIdentificacionSujetoRetenido")]
        public string TipoIdentificacionSujetoRetenido { get; set; }
        [XmlElement(ElementName = "razonSocialSujetoRetenido")]
        public string RazonSocialSujetoRetenido { get; set; }
        [XmlElement(ElementName = "identificacionSujetoRetenido")]
        public string IdentificacionSujetoRetenido { get; set; }
        [XmlElement(ElementName = "periodoFiscal")]
        public string PeriodoFiscal { get; set; }
    }

    [XmlRoot(ElementName = "impuesto")]
    public class ImpuestoRetencion
    {
        [XmlElement(ElementName = "codigo")]
        public string Codigo { get; set; }
        [XmlElement(ElementName = "codigoRetencion")]
        public string CodigoRetencion { get; set; }
        [XmlElement(ElementName = "baseImponible")]
        public string BaseImponible { get; set; }
        [XmlElement(ElementName = "porcentajeRetener")]
        public string PorcentajeRetener { get; set; }
        [XmlElement(ElementName = "valorRetenido")]
        public string ValorRetenido { get; set; }
        [XmlElement(ElementName = "codDocSustento")]
        public string CodDocSustento { get; set; }
        [XmlElement(ElementName = "numDocSustento")]
        public string NumDocSustento { get; set; }
        [XmlElement(ElementName = "fechaEmisionDocSustento")]
        public string FechaEmisionDocSustento { get; set; }
    }

    [XmlRoot(ElementName = "impuestos")]
    public class ImpuestosRetencion
    {
        [XmlElement(ElementName = "impuesto")]
        public List<ImpuestoRetencion> Impuesto { get; set; }
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class MaquinaFiscalRetencion
    {
        [XmlElement(ElementName = "marca")]
        public string Marca { get; set; }
        [XmlElement(ElementName = "modelo")]
        public string Modelo { get; set; }
        [XmlElement(ElementName = "serie")]
        public string Serie { get; set; }
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CampoAdicionalRetencion
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InfoAdicionalRetencion
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CampoAdicionalRetencion> CampoAdicional { get; set; }
    }

    [XmlRoot(ElementName = "comprobanteRetencion")]
    public class ComprobanteRetencion
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InfoTributariaRetencion InfoTributaria { get; set; }
        [XmlElement(ElementName = "infoCompRetencion")]
        public InfoCompRetencion InfoCompRetencion { get; set; }
        [XmlElement(ElementName = "impuestos")]
        public ImpuestosRetencion Impuestos { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public MaquinaFiscalRetencion MaquinaFiscal { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InfoAdicionalRetencion InfoAdicional { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }
    }

}
