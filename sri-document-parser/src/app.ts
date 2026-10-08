import Fastify from 'fastify';
import { convertXml, ParseFailure } from './parser.js';

// The dependency logs caught errors. Avoid leaking XML through its console diagnostics.
console.error = () => {};
export function buildApp(maxBytes = 20 * 1024 * 1024, converter = convertXml) {
  const app = Fastify({ logger: false, bodyLimit: maxBytes });
  let busy = false;
  app.get('/health', async () => ({ status: 'ready', parserName: 'taxo-sri-xml-2-json', parserVersion: '1.8.0' }));
  app.post<{ Body: { requestId: string; xml: string } }>('/parse', {
    schema: { body: { type: 'object', required: ['requestId', 'xml'], additionalProperties: false,
      properties: { requestId: { type: 'string', minLength: 1, maxLength: 128 }, xml: { type: 'string', minLength: 1 } } } }
  }, async (request, reply) => {
    if (busy) return reply.code(503).send({ code: 'parserBusy', requestId: request.body.requestId });
    if (Buffer.byteLength(request.body.xml) > maxBytes) return reply.code(413).send({ code: 'inputTooLarge' });
    busy = true;
    try { return { requestId: request.body.requestId, ...await converter(request.body.xml) }; }
    catch (error) {
      const known = error instanceof ParseFailure;
      return reply.code(known ? error.statusCode : 500).send({ requestId: request.body.requestId,
        code: known ? error.code : 'parserFailed', message: 'Document conversion could not be completed.' });
    } finally { busy = false; }
  });
  app.setErrorHandler((error, _request, reply) => {
    const failure = error as { statusCode?: number; validation?: unknown };
    const status = failure.statusCode === 413 ? 413 : failure.validation || failure.statusCode === 400 ? 400 : 500;
    reply.code(status).send({ code: status === 413 ? 'inputTooLarge' : status === 400 ? 'invalidRequest' : 'parserFailed' });
  });
  return app;
}
