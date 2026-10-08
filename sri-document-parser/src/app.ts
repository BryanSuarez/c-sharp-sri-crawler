import Fastify from 'fastify';
import { convertXml, ParseFailure } from './parser.js';

// The dependency logs caught errors. Avoid leaking XML through its console diagnostics.
console.error = () => {};
export function buildApp(maxBytes = 20 * 1024 * 1024, converter = convertXml,
  writeLog: (entry: Record<string, unknown>) => void = entry => { if (entry.level !== 'debug' || process.env.LOG_LEVEL?.toLowerCase() === 'debug') process.stdout.write(JSON.stringify(entry) + '\n'); }) {
  const app = Fastify({ logger: false, bodyLimit: maxBytes * 6 + 1024 });
  let busy = false;
  app.get('/health', async () => ({ status: 'ready', parserName: 'taxo-sri-xml-2-json', parserVersion: '1.8.0' }));
  app.post<{ Body: { requestId: string; xml: string } }>('/parse', {
    schema: { body: { type: 'object', required: ['requestId', 'xml'], additionalProperties: false,
      properties: { requestId: { type: 'string', minLength: 1, maxLength: 128 }, xml: { type: 'string', minLength: 1 } } } }
  }, async (request, reply) => {
    const started = performance.now();
    let outcome = 'failed'; let code: string | null = null;
    const logResult = () => {
      try { writeLog({ timestamp: new Date().toISOString(), level: outcome === 'parsed' ? 'debug' : 'warning',
        event: 'parserConversion', requestId: /^[a-zA-Z0-9_-]{1,128}$/.test(request.body.requestId) ? request.body.requestId : null,
        durationMs: performance.now() - started, outcome, errorCode: code, statusCode: reply.statusCode }); } catch { }
    };
    if (busy) { code = 'parserBusy'; reply.code(503); logResult(); return reply.send({ code, requestId: request.body.requestId }); }
    if (Buffer.byteLength(request.body.xml) > maxBytes) { code = 'inputTooLarge'; reply.code(413); logResult(); return reply.send({ code }); }
    busy = true;
    try { const result = await converter(request.body.xml); outcome = 'parsed'; return { requestId: request.body.requestId, ...result }; }
    catch (error) {
      const known = error instanceof ParseFailure;
      code = known ? error.code : 'parserFailed';
      return reply.code(known ? error.statusCode : 500).send({ requestId: request.body.requestId,
        code, message: 'Document conversion could not be completed.' });
    } finally { busy = false; logResult(); }
  });
  app.setErrorHandler((error, _request, reply) => {
    const failure = error as { statusCode?: number; validation?: unknown };
    const status = failure.statusCode === 413 ? 413 : failure.validation || failure.statusCode === 400 ? 400 : 500;
    try { writeLog({ timestamp: new Date().toISOString(), level: 'warning', event: 'parserRejected',
      outcome: 'failed', errorCode: status === 413 ? 'inputTooLarge' : status === 400 ? 'invalidRequest' : 'parserFailed', statusCode: status }); } catch { }
    reply.code(status).send({ code: status === 413 ? 'inputTooLarge' : status === 400 ? 'invalidRequest' : 'parserFailed' });
  });
  return app;
}
