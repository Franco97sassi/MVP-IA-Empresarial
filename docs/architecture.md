# Arquitectura de LocalMind AI

## Vista general

LocalMind AI separa responsabilidades en frontend, backend, almacenamiento RAG local y Ollama.

```text
React/Vite
  ↓ JWT + API REST
ASP.NET Core
  ├─ Auth/JWT
  ├─ Chat orchestration
  ├─ Tools
  ├─ RAG
  ├─ Metrics
  └─ Security middleware
  ↓
SQLite + rag/
  ↓
Ollama chat + embeddings
```

## Orquestación

El backend decide la ruta de respuesta:

1. Si detecta una tool y no parece pregunta sobre documentos, ejecuta la tool.
2. Si no aplica tool, busca chunks relevantes del usuario.
3. Si encuentra contexto, re-rankea chunks con score híbrido (vector + keywords), aplica diversidad por documento y responde con fuentes.
4. Si no encuentra contexto, usa chat normal con Ollama.

## Métricas

Cada request exitoso o fallido dentro del flujo de chat registra:

- modelo usado
- tiempo de respuesta
- tokens aproximados
- uso de RAG
- uso de tool
- chunks usados
- ruta (`chat`, `rag`, `tool`, `error`)
- errores
- fecha

## Seguridad básica

- JWT para rutas protegidas.
- Límite de 4000 caracteres por mensaje.
- Validación de extensión y `Content-Type` para documentos.
- Sanitización de nombres de archivo.
- Middleware global de errores con respuestas `ProblemDetails`.
- Bloqueo básico de frases comunes de prompt injection.


## RAG etapa 7

- El índice principal persiste documentos, chunks y embeddings serializados en SQLite + `rag/`.
- La recuperación acepta filtros por documento, `MinSimilarityScore` y `MaxRetrievedChunks`.
- El ranking final combina 75% similitud vectorial y 25% coincidencia léxica, con un paso de diversidad para evitar que un solo documento monopolice todas las fuentes.
- Cada fuente devuelta al frontend incluye referencia `doc:{id}#chunk:{n}`, score vectorial, score de keywords, rank final y preview del chunk.
- `POST /api/documents/rag/evaluate` permite ejecutar evaluaciones pequeñas con preguntas, documentos esperados y umbrales para medir cobertura y hit rate.
- Docker Compose levanta Qdrant para alinear el entorno con una arquitectura vectorial real; por ahora `Rag:VectorStoreProvider=Local` mantiene el fallback local como modo principal.
docs/roadmap.md
docs/roadmap.md