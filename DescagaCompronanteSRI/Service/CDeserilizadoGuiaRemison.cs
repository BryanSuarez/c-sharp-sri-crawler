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
    public class InfoTributariaGuiaRemision
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

    [XmlRoot(ElementName = "infoGuiaRemision")]
    public class InfoGuiaRemisionGuiaRemision
    {
        [XmlElement(ElementName = "dirEstablecimiento")]
        public string DirEstablecimiento { get; set; }
        [XmlElement(ElementName = "dirPartida")]
        public string DirPartida { get; set; }
        [XmlElement(ElementName = "razonSocialTransportista")]
        public string RazonSocialTransportista { get; set; }
        [XmlElement(ElementName = "tipoIdentificacionTransportista")]
        public string TipoIdentificacionTransportista { get; set; }
        [XmlElement(ElementName = "rucTransportista")]
        public string RucTransportista { get; set; }
        [XmlElement(ElementName = "rise")]
        public string Rise { get; set; }
        [XmlElement(ElementName = "obligadoContabilidad")]
        public string ObligadoContabilidad { get; set; }
        [XmlElement(ElementName = "contribuyenteEspecial")]
        public string ContribuyenteEspecial { get; set; }
        [XmlElement(ElementName = "fechaIniTransporte")]
        public string FechaIniTransporte { get; set; }
        [XmlElement(ElementName = "fechaFinTransporte")]
        public string FechaFinTransporte { get; set; }
        [XmlElement(ElementName = "placa")]
        public string Placa { get; set; }
    }

    [XmlRoot(ElementName = "detalle")]
    public class DetalleGuiaRemision
    {
        [XmlElement(ElementName = "codigoInterno")]
        public string CodigoInterno { get; set; }
        [XmlElement(ElementName = "codigoAdicional")]
        public string CodigoAdicional { get; set; }
        [XmlElement(ElementName = "descripcion")]
        public string Descripcion { get; set; }
        [XmlElement(ElementName = "cantidad")]
        public string Cantidad { get; set; }
        [XmlElement(ElementName = "detallesAdicionales")]
        public string DetallesAdicionales { get; set; }
    }

    [XmlRoot(ElementName = "detalles")]
    public class DetallesGuiaRemision
    {
        [XmlElement(ElementName = "detalle")]
        public List<DetalleGuiaRemision> Detalle { get; set; }
    }

    [XmlRoot(ElementName = "destinatario")]
    public class DestinatarioGuiaRemision
    {
        [XmlElement(ElementName = "identificacionDestinatario")]
        public string IdentificacionDestinatario { get; set; }
        [XmlElement(ElementName = "razonSocialDestinatario")]
        public string RazonSocialDestinatario { get; set; }
        [XmlElement(ElementName = "dirDestinatario")]
        public string DirDestinatario { get; set; }
        [XmlElement(ElementName = "motivoTraslado")]
        public string MotivoTraslado { get; set; }
        [XmlElement(ElementName = "docAduaneroUnico")]
        public string DocAduaneroUnico { get; set; }
        [XmlElement(ElementName = "codEstabDestino")]
        public string CodEstabDestino { get; set; }
        [XmlElement(ElementName = "ruta")]
        public string Ruta { get; set; }
        [XmlElement(ElementName = "codDocSustento")]
        public string CodDocSustento { get; set; }
        [XmlElement(ElementName = "numDocSustento")]
        public string NumDocSustento { get; set; }
        [XmlElement(ElementName = "numAutDocSustento")]
        public string NumAutDocSustento { get; set; }
        [XmlElement(ElementName = "fechaEmisionDocSustento")]
        public string FechaEmisionDocSustento { get; set; }
        [XmlElement(ElementName = "detalles")]
        public DetallesGuiaRemision Detalles { get; set; }
    }

    [XmlRoot(ElementName = "destinatarios")]
    public class DestinatariosGuiaRemision
    {
        [XmlElement(ElementName = "destinatario")]
        public List<DestinatarioGuiaRemision> Destinatario { get; set; }
    }

    [XmlRoot(ElementName = "maquinaFiscal")]
    public class MaquinaFiscalGuiaRemision
    {
        [XmlElement(ElementName = "marca")]
        public string Marca { get; set; }
        [XmlElement(ElementName = "modelo")]
        public string Modelo { get; set; }
        [XmlElement(ElementName = "serie")]
        public string Serie { get; set; }
    }

    [XmlRoot(ElementName = "campoAdicional")]
    public class CampoAdicionalGuiaRemision
    {
        [XmlAttribute(AttributeName = "nombre")]
        public string Nombre { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "infoAdicional")]
    public class InfoAdicionalGuiaRemision
    {
        [XmlElement(ElementName = "campoAdicional")]
        public List<CampoAdicionalGuiaRemision> CampoAdicional { get; set; }
    }

    [XmlRoot(ElementName = "guiaRemision")]
    public class GuiaRemision
    {
        [XmlElement(ElementName = "infoTributaria")]
        public InfoTributariaGuiaRemision InfoTributaria { get; set; }
        [XmlElement(ElementName = "infoGuiaRemision")]
        public InfoGuiaRemisionGuiaRemision InfoGuiaRemision { get; set; }
        [XmlElement(ElementName = "destinatarios")]
        public DestinatariosGuiaRemision Destinatarios { get; set; }
        [XmlElement(ElementName = "maquinaFiscal")]
        public MaquinaFiscalGuiaRemision MaquinaFiscal { get; set; }
        [XmlElement(ElementName = "infoAdicional")]
        public InfoAdicional InfoAdicional { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }
    }

}
