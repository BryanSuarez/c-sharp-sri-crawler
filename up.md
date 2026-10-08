Desde **Warp**, con Docker Desktop abierto:

```bash
cd /Users/bryansuarez/Projects/sri-crawler
docker compose --env-file .env.jobs up --build -d
```

Esto levanta la API, el worker, el parser y PostgreSQL. Los `.env` ya están configurados.

Para ver los logs en tu consola:

```bash
docker compose --env-file .env.jobs logs -f sri-descarga worker parser
```

Abre [Swagger](http://localhost:8080/swagger/index.html) y prueba tus peticiones contra `http://localhost:8080`. El navegador del crawler corre dentro de Docker.

Para apagar todo conservando los datos:

```bash
docker compose --env-file .env.jobs stop
```

`Ctrl+C` en la consola de logs solo deja de mostrarlos; los servicios siguen ejecutándose.