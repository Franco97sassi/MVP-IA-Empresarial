# Arquitectura RAG de prueba

LocalMind usa RAG para responder preguntas sobre documentos cargados.
El ranking combina similitud vectorial con coincidencias de palabras clave.
La respuesta debe mostrar fuentes, chunk usado, score vectorial, score keyword y preview.

Qdrant puede usarse como vector store opcional, pero esta prueba usa el índice local.
