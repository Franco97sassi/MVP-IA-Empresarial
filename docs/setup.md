# Setup de LocalMind AI

## Ollama

```bash
ollama pull qwen2.5-coder:7b
ollama pull nomic-embed-text
ollama serve
```

## Backend

```bash
cd backend/LocalMind.Api
dotnet restore
dotnet ef database update
dotnet run
```

Variables importantes:

- `ConnectionStrings__DefaultConnection`
- `Jwt__Key`
- `Ollama__BaseUrl`
- `Ollama__Model`
- `Ollama__EmbeddingModel`
- `Rag__StorageRoot`
- `Rag__MinSimilarityScore`
- `Rag__MaxRetrievedChunks`
- `Rag__VectorStoreProvider` (`Local` por defecto)
- `Rag__QdrantUrl` y `Rag__QdrantCollection` para preparar integración Qdrant
- `Security__Chat__MaxMessageLength`

## Frontend

```bash
cd frontend
npm install
npm run dev
```

## Docker

```bash
docker compose up --build
```

Si Ollama está en Docker, descargá los modelos dentro del contenedor o mantené el volumen `ollama-data` persistente.


## Validación rápida

```bash
dotnet build backend/LocalMind.Api/LocalMind.Api.csproj
npm run build --prefix frontend

## Evaluaciones RAG rápidas

Con sesión autenticada, enviá hasta 20 preguntas al endpoint:

```bash
curl -X POST http://localhost:5201/api/documents/rag/evaluate \
  -H "Authorization: Bearer TU_TOKEN" \
  -H "Content-Type: application/json" \
  -d '[{"query":"¿Qué dice el documento sobre RAG?","expectedDocumentIds":[1],"maxRetrievedChunks":4}]'
```

La respuesta resume cobertura, hit rate contra documentos esperados, score superior promedio y los chunks recuperados con sus previews.