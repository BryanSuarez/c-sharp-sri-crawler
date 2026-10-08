import { DOMParser, XMLSerializer } from '@xmldom/xmldom';
import { Ride } from 'taxo-sri-xml-2-json';

const signatureNamespace = 'http://www.w3.org/2000/09/xmldsig#';
const supported = new Set(['factura', 'liquidacionCompra', 'notaCredito', 'notaDebito', 'comprobanteRetencion']);
export class ParseFailure extends Error {
  constructor(public readonly code: string, public readonly statusCode: number) { super(code); }
}
function document(xml: string) {
  if (/<!DOCTYPE\b|<!ENTITY\b/i.test(xml)) throw new ParseFailure('unsafeXml', 400);
  try {
    return new DOMParser({ onError: () => { throw new Error('Invalid XML'); } }).parseFromString(xml, 'text/xml');
  } catch { throw new ParseFailure('invalidXml', 400); }
}

export function prepareXml(xml: string): { xml: string; documentType: string } {
  const outer = document(xml);
  const root = outer.documentElement;
  if (!root) throw new ParseFailure('invalidXml', 400);
  const authorization = root.localName === 'autorizacion' || root.localName === 'Authorization';
  const receiptNode = authorization ? root.getElementsByTagName('comprobante').item(0) : null;
  if (authorization && !receiptNode) throw new ParseFailure('invalidAuthorization', 400);
  const embedded = receiptNode?.firstChild?.nodeType === 1
    ? new XMLSerializer().serializeToString(receiptNode.firstChild)
    : receiptNode?.textContent;
  const receipt = authorization ? document(embedded ?? '') : outer;
  const type = receipt.documentElement?.localName;
  if (!type || !supported.has(type)) throw new ParseFailure('unsupportedDocumentType', 422);
  const signatures = receipt.getElementsByTagNameNS(signatureNamespace, 'Signature');
  while (signatures.length) {
    const node = signatures.item(0)!;
    node.parentNode?.removeChild(node);
  }
  const content = new XMLSerializer().serializeToString(receipt);
  const wrapper = document('<autorizacion><comprobante/></autorizacion>');
  wrapper.getElementsByTagName('comprobante').item(0)!.appendChild(wrapper.createTextNode(content));
  // Preserve supplied authorization metadata without inventing a successful status/date.
  if (authorization) {
    for (const name of ['fechaAutorizacion', 'estado']) {
      const source = root.getElementsByTagName(name).item(0);
      if (source) {
        const node = wrapper.createElement(name);
        node.appendChild(wrapper.createTextNode(source.textContent ?? ''));
        wrapper.documentElement!.appendChild(node);
      }
    }
  }
  return { xml: new XMLSerializer().serializeToString(wrapper), documentType: type };
}

export async function convertXml(xml: string) {
  const prepared = prepareXml(xml);
  let json: unknown;
  try { json = JSON.parse(await new Ride(prepared.xml).convertToJson()); }
  catch { throw new ParseFailure('conversionFailed', 400); }
  return { status: 'parsed', parserName: 'taxo-sri-xml-2-json', parserVersion: '1.8.0',
    documentType: prepared.documentType, documentJson: json };
}
