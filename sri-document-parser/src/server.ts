import { buildApp } from './app.js';
const app = buildApp(Number(process.env.PARSER_MAX_INPUT_BYTES ?? 20 * 1024 * 1024));
await app.listen({ host: '0.0.0.0', port: Number(process.env.PORT ?? 3000) });
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, async () => { await app.close(); process.exit(0); });
