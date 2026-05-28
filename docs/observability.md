# Observabilidad avanzada

## Trazas y métricas

El backend ahora expone instrumentación con OpenTelemetry para:

- ASP.NET Core requests
- HttpClient saliente (incluyendo llamadas a Ollama)
- Runtime metrics

La salida por defecto va a consola con `AddConsoleExporter`.

## Dashboards operativos sugeridos

- Latencia p50/p95/p99 por endpoint (`/api/chat`, `/api/documents/upload`, `/api/documents/{id}/reindex`)
- Error rate por ruta
- Throughput por usuario autenticado
- Tiempo de embedding y recuperación RAG
- Saturación de CPU/memoria del proceso

## Próximo paso recomendado

Conectar OTEL a un stack como Grafana Tempo + Prometheus + Loki o Azure Monitor para tener dashboards persistentes y alertas.
