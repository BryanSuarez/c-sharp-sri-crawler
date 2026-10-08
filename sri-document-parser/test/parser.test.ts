import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildApp } from '../src/app.js';
import { prepareXml, convertXml } from '../src/parser.js';

const key = '1'.repeat(49);
const taxInfo = `<infoTributaria><ambiente>2</ambiente><tipoEmision>1</tipoEmision><razonSocial>Fixture issuer</razonSocial><ruc>1719956854001</ruc><claveAcceso>${key}</claveAcceso><codDoc>01</codDoc><estab>001</estab><ptoEmi>001</ptoEmi><secuencial>000000001</secuencial><dirMatriz>Fixture</dirMatriz></infoTributaria>`;
const totals = '<totalConImpuestos><totalImpuesto><codigo>2</codigo><codigoPorcentaje>4</codigoPorcentaje><baseImponible>1</baseImponible><valor>0.15</valor></totalImpuesto></totalConImpuestos>';
const details = '<detalles><detalle><codigoPrincipal>0001</codigoPrincipal><descripcion>Fixture</descripcion><cantidad>1</cantidad><precioUnitario>1</precioUnitario><precioTotalSinImpuesto>1</precioTotalSinImpuesto><impuestos><impuesto><codigo>3</codigo><codigoPorcentaje>3093</codigoPorcentaje><tarifa>0</tarifa><baseImponible>0</baseImponible><valor>0</valor></impuesto><impuesto><codigo>2</codigo><codigoPorcentaje>4</codigoPorcentaje><tarifa>15</tarifa><baseImponible>1</baseImponible><valor>0.15</valor></impuesto></impuestos></detalle></detalles>';
const payment = '<pagos><pago><formaPago>01</formaPago><total>0.50</total></pago><pago><formaPago>19</formaPago><total>0.65</total></pago></pagos>';
const invoice = `<factura id="comprobante" version="1.1.0">${taxInfo}<infoFactura><fechaEmision>01/09/2026</fechaEmision><tipoIdentificacionComprador>05</tipoIdentificacionComprador><identificacionComprador>0012345678</identificacionComprador><importeTotal>1.15</importeTotal>${totals}${payment}</infoFactura>${details}<sig:Signature xmlns:sig="http://www.w3.org/2000/09/xmldsig#"><sig:SignatureValue>fixture-signature</sig:SignatureValue></sig:Signature></factura>`;

async function parse(xml: string) {
  const app = buildApp();
  try { return await app.inject({ method: 'POST', url: '/parse', payload: { requestId: 'fixture', xml } }); }
  finally { await app.close(); }
}
test('real library parses receipt, preserves access key and multiple item taxes, strips alternative signature prefix', async () => {
  const response = await parse(invoice);
  assert.equal(response.statusCode, 200, response.body);
  const json = response.json();
  assert.equal(json.parserVersion, '1.8.0');
  assert.equal(json.documentJson.infoTributaria.claveAcceso, key);
  assert.equal(json.documentJson.infoDocumento.identificacionComprador, '0012345678');
  assert.equal(json.documentJson.productos[0].impuestos.length, 2);
  assert.equal(JSON.stringify(json).includes('fixture-signature'), false);
  // Document current dependency behavior; do not silently normalize multiple payments.
  assert.equal(json.documentJson.infoDocumento.pago['0'].formaPago, '01');
  assert.match(invoice, /fixture-signature/);
});
test('authorization metadata survives conversion, absent metadata is not invented', async () => {
  const wrapped = `<autorizacion><estado>AUTORIZADO</estado><fechaAutorizacion>2026-09-01T12:00:00</fechaAutorizacion><comprobante><![CDATA[${invoice}]]></comprobante></autorizacion>`;
  const response = await parse(wrapped);
  assert.equal(response.statusCode, 200);
  assert.equal(response.json().documentJson.estado, 'AUTORIZADO');
  const raw = await parse(invoice);
  assert.equal(raw.json().documentJson.estado, undefined);
});
for (const [root, info, content] of [
  ['notaCredito', 'infoNotaCredito', `${totals}<tipoIdentificacionComprador>05</tipoIdentificacionComprador>`],
  ['liquidacionCompra', 'infoLiquidacionCompra', `${totals}<tipoIdentificacionProveedor>05</tipoIdentificacionProveedor>`],
  ['notaDebito', 'infoNotaDebito', '<tipoIdentificacionComprador>05</tipoIdentificacionComprador><impuestos><impuesto><codigo>2</codigo><codigoPorcentaje>4</codigoPorcentaje><valor>1</valor></impuesto></impuestos>'],
  ['comprobanteRetencion', 'infoCompRetencion', '<tipoIdentificacionSujetoRetenido>04</tipoIdentificacionSujetoRetenido>']
]) {
  test(`real library supports ${root}`, async () => {
    const xml = `<${root} id="comprobante" version="1.0.0">${taxInfo}<${info}>${content}</${info}>${details}</${root}>`;
    const response = await parse(xml);
    assert.equal(response.statusCode, 200, response.body);
    assert.equal(response.json().documentType, root);
  });
}
test('malformed XML, DTD and embedded DTD are rejected', async () => {
  for (const xml of ['<factura>', '<!DOCTYPE factura [<!ENTITY x SYSTEM "file:///etc/passwd">]><factura/>', '<autorizacion><comprobante><![CDATA[<!DOCTYPE factura><factura/>]]></comprobante></autorizacion>'])
    assert.equal((await parse(xml)).statusCode, 400);
});
test('unsupported guide is explicit, oversized bodies are rejected and health is ready', async () => {
  assert.equal((await parse('<guiaRemision/>')).statusCode, 422);
  const app = buildApp(100);
  try {
    assert.equal((await app.inject({ method: 'POST', url: '/parse', payload: { requestId: 'size', xml: invoice } })).statusCode, 413);
    assert.equal((await app.inject({ url: '/health' })).json().status, 'ready');
  } finally { await app.close(); }
});
test('signature stripping is namespace aware and leaves non-signature business fields', () => {
  const prepared = prepareXml(invoice.replaceAll('sig:', 'ds:').replace('xmlns:sig', 'xmlns:ds'));
  assert.equal(prepared.xml.includes('fixture-signature'), false);
  assert.equal(prepared.xml.includes('Fixture issuer'), true);
});

test('a busy instance rejects extra conversions and stays available for health checks', async () => {
  let started!: () => void;
  const active = new Promise<void>(resolve => { started = resolve; });
  let finish!: () => void;
  const blocked = new Promise<void>(resolve => { finish = resolve; });
  const app = buildApp(undefined, async xml => { started(); await blocked; return convertXml(xml); });
  try {
    const first = app.inject({ method: 'POST', url: '/parse', payload: { requestId: 'first', xml: invoice } });
    await active;
    const extra = await app.inject({ method: 'POST', url: '/parse', payload: { requestId: 'extra', xml: invoice } });
    assert.equal(extra.statusCode, 503);
    assert.equal(extra.json().code, 'parserBusy');
    assert.equal((await app.inject({ url: '/health' })).statusCode, 200);
    finish();
    assert.equal((await first).statusCode, 200);
  } finally { finish(); await app.close(); }
});


test('XML byte quota is independent of JSON escaping overhead', async () => {
  const xml = `<factura>${'"\\'.repeat(100)}</factura>`;
  const maxBytes = Buffer.byteLength(xml);
  const app = buildApp(maxBytes, async () => ({ status: 'parsed', parserName: 'fixture', parserVersion: '1', documentType: 'factura', documentJson: {} }));
  try {
    const response = await app.inject({ method: 'POST', url: '/parse', payload: { requestId: 'escaping', xml } });
    assert.equal(response.statusCode, 200, response.body);
    assert.equal((await app.inject({ method: 'POST', url: '/parse', payload: { requestId: 'escaping', xml: xml + 'x' } })).statusCode, 413);
  } finally { await app.close(); }
});
