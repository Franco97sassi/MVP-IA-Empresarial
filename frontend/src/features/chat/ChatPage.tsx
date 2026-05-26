import { useEffect, useMemo, useState } from "react";
import axios from "axios";
import {
  deleteConversation,
  exportConversation,
  getConversation,
  getHistory,
  renameConversation,
  sendMessageStream,
  type ChatMessage,
  type ConversationHistoryItem,
} from "./chatService";
import {
  deleteDocument,
  getDocuments,
  uploadDocument,
  type DocumentItem,
} from "../documents/documentService";

const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024;
const ACCEPTED_FILE_EXTENSIONS = [".pdf", ".txt", ".md"];

function getRequestErrorMessage(error: unknown, fallback: string) {
  if (!axios.isAxiosError(error)) {
    return fallback;
  }

  if (!error.response) {
    return "No se pudo conectar con el backend.";
  }

  const data = error.response.data;

  if (typeof data === "string" && data.trim().length > 0) {
    return data;
  }

  if (data?.message) {
    return data.message;
  }

  if (data?.error) {
    return data.error;
  }

  return fallback;
}

function getRouteBadge(message: ChatMessage) {
  if (message.usedTool) {
    return `Tool: ${message.toolName ?? "herramienta"}`;
  }

  if (message.route === "rag") {
    return "RAG con fuentes";
  }

  if (message.route === "chat") {
    return "Chat general";
  }

  return null;
}

export default function ChatPage() {
  const [message, setMessage] = useState("");
  const [conversationId, setConversationId] = useState<number | null>(null);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [history, setHistory] = useState<ConversationHistoryItem[]>([]);
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [selectedDocumentIds, setSelectedDocumentIds] = useState<number[]>([]);

  const [isSending, setIsSending] = useState(false);
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamAbortController, setStreamAbortController] =
    useState<AbortController | null>(null);

  const [isUploading, setIsUploading] = useState(false);
  const [deletingDocumentId, setDeletingDocumentId] = useState<number | null>(
    null
  );

  const [chatError, setChatError] = useState<string | null>(null);
    const [historySearch, setHistorySearch] = useState("");
  const [documentError, setDocumentError] = useState<string | null>(null);
  const [documentNotice, setDocumentNotice] = useState<string | null>(null);

  const canSendMessage = useMemo(() => {
    return message.trim().length > 0 && !isSending && !isStreaming;
  }, [message, isSending, isStreaming]);
const groupedHistory = useMemo(() => {
    const filtered = history.filter((item) => {
      const q = historySearch.trim().toLowerCase();
      if (!q) return true;
      return item.title.toLowerCase().includes(q) || String(item.id).includes(q);
    });

    return filtered.reduce<Record<string, ConversationHistoryItem[]>>((acc, item) => {
      const key = new Date(item.createdAt).toLocaleDateString();
      acc[key] = acc[key] ?? [];
      acc[key].push(item);
      return acc;
    }, {});
  }, [history, historySearch]);

  const selectedDocumentsLabel = useMemo(() => {
    if (selectedDocumentIds.length === 0) {
      return "todos los documentos";
    }

    return `${selectedDocumentIds.length} seleccionado${
      selectedDocumentIds.length === 1 ? "" : "s"
    }`;
  }, [selectedDocumentIds.length]);

  const loadHistory = async () => {
    const data = await getHistory();
    setHistory(data);
  };

  const loadDocuments = async () => {
    const data = await getDocuments();
    setDocuments(data);
  };

  useEffect(() => {
    Promise.all([loadHistory(), loadDocuments()]).catch((error) => {
      console.error("Error cargando datos iniciales:", error);
      setChatError("No se pudieron cargar el historial y los documentos.");
    });
  }, []);

  const handleNewConversation = () => {
    setConversationId(null);
    setMessages([]);
    setChatError(null);
  };

  const handleSelectConversation = async (id: number) => {
    try {
      setChatError(null);

      const data = await getConversation(id);

      setConversationId(data.id);
      setMessages(data.messages);
    } catch (error) {
      console.error("Error cargando conversación:", error);
      setChatError(
        getRequestErrorMessage(error, "No se pudo cargar la conversación.")
      );
    }
  };

  const toggleDocumentSelection = (documentId: number) => {
    setSelectedDocumentIds((previousIds) => {
      if (previousIds.includes(documentId)) {
        return previousIds.filter((id) => id !== documentId);
      }

      return [...previousIds, documentId];
    });
  };

  const clearDocumentFilter = () => {
    setSelectedDocumentIds([]);
  };

  const handleUploadDocument = async (
    event: React.ChangeEvent<HTMLInputElement>
  ) => {
    const file = event.target.files?.[0];

    if (!file) {
      return;
    }

    setDocumentError(null);
    setDocumentNotice(null);

    const fileExtension = file.name
      .slice(file.name.lastIndexOf("."))
      .toLowerCase();

    if (!ACCEPTED_FILE_EXTENSIONS.includes(fileExtension)) {
      setDocumentError("Solo se aceptan archivos PDF, TXT o MD.");
      event.target.value = "";
      return;
    }

    if (file.size > MAX_FILE_SIZE_BYTES) {
      setDocumentError("El archivo no puede superar los 10 MB.");
      event.target.value = "";
      return;
    }

    try {
      setIsUploading(true);
      await uploadDocument(file);
      await loadDocuments();
      setDocumentNotice("Documento subido correctamente.");
    } catch (error) {
      console.error("Error subiendo documento:", error);
      setDocumentError(
        getRequestErrorMessage(error, "No se pudo subir el documento.")
      );
    } finally {
      setIsUploading(false);
      event.target.value = "";
    }
  };

  const handleDeleteDocument = async (documentId: number) => {
    try {
      setDeletingDocumentId(documentId);
      setDocumentError(null);
      setDocumentNotice(null);

      await deleteDocument(documentId);

      setSelectedDocumentIds((previousIds) =>
        previousIds.filter((id) => id !== documentId)
      );

      await loadDocuments();

      setDocumentNotice("Documento eliminado correctamente.");
    } catch (error) {
      console.error("Error eliminando documento:", error);
      setDocumentError(
        getRequestErrorMessage(error, "No se pudo eliminar el documento.")
      );
    } finally {
      setDeletingDocumentId(null);
    }
  };

  const handleSend = async (event: React.FormEvent) => {
    event.preventDefault();

    if (!canSendMessage) {
      return;
    }

    const userMessage = message.trim();

    setMessages((previousMessages) => [
      ...previousMessages,
      {
        role: "user",
        content: userMessage,
      },
    ]);

    setMessage("");
    setIsSending(true);
    setIsStreaming(true);
    setChatError(null);

    const controller = new AbortController();
    setStreamAbortController(controller);

    setMessages((previousMessages) => [
      ...previousMessages,
      {
        role: "assistant",
        content: "",
      },
    ]);

    try {
      const token = localStorage.getItem("localmind_token") ?? "";

      await sendMessageStream(
        {
          conversationId,
          message: userMessage,
          documentIds: selectedDocumentIds,
        },
        token,
        {
          onMeta: (newConversationId) => {
            setConversationId(newConversationId);
          },
          onChunk: (chunk) => {
            setMessages((previousMessages) => {
              const next = [...previousMessages];
              const last = next[next.length - 1];

              if (last?.role === "assistant") {
                last.content += chunk;
              }

              return next;
            });
          },
          onDone: (data) => {
            setMessages((previousMessages) => {
              const next = [...previousMessages];
              const last = next[next.length - 1];

              if (last?.role === "assistant") {
                last.sources = data.sources;
                last.usedTool = data.usedTool;
                last.toolName = data.toolName;
                last.route = data.route;
              }

              return next;
            });
          },
        },
        controller.signal
      );

      await loadHistory();
    } catch (error) {
      console.error("Error enviando mensaje:", error);

      const errorMessage = getRequestErrorMessage(
        error,
        "No se pudo procesar el mensaje."
      );

      setChatError(errorMessage);

      setMessages((previousMessages) => {
        const next = [...previousMessages];
        const last = next[next.length - 1];

        if (last?.role === "assistant" && last.content.length === 0) {
          last.content = errorMessage;
          return next;
        }

        return [
          ...previousMessages,
          {
            role: "assistant",
            content: errorMessage,
          },
        ];
      });
    } finally {
      setIsSending(false);
      setIsStreaming(false);
      setStreamAbortController(null);
    }
  };
 const handleRenameConversation = async (id: number, currentTitle: string) => {
    const title = window.prompt("Nuevo nombre de la conversación", currentTitle);
    if (!title) return;
    await renameConversation(id, title);
    await loadHistory();
  };

  const handleDeleteConversation = async (id: number) => {
    if (!window.confirm("¿Eliminar conversación?")) return;
    await deleteConversation(id);
    if (conversationId === id) handleNewConversation();
    await loadHistory();
  };

  const handleExportConversation = async (id: number) => {
    const content = await exportConversation(id);
    const blob = new Blob([content], { type: "text/markdown;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `conversation-${id}.md`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const handleLogout = () => {
    localStorage.removeItem("localmind_token");
    localStorage.removeItem("localmind_email");
    window.location.href = "/login";
  };

  return (
    <main className="flex h-screen bg-slate-950 text-slate-100">
      <aside className="flex w-80 flex-col border-r border-slate-800 p-4">
        <section className="mb-6">
          <div className="mb-3 flex items-center justify-between gap-2">
            <input value={historySearch} onChange={(e) => setHistorySearch(e.target.value)} placeholder="Buscar..." className="ml-2 rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs" />
            <h2 className="text-sm font-semibold">Conversaciones</h2>

            <button
              type="button"
              onClick={handleNewConversation}
              className="rounded border border-slate-700 px-2 py-1 text-xs hover:bg-slate-800"
            >
              Nueva
            </button>
          </div>

          <div className="flex max-h-64 flex-col gap-2 overflow-y-auto">
            {history.length === 0 && (
              <p className="text-xs text-slate-500">
                Todavía no hay conversaciones.
              </p>
            )}

            {Object.entries(groupedHistory).map(([groupDate, items]) => (
              <div key={groupDate}>
                <p className="mb-1 text-[11px] uppercase text-slate-500">{groupDate}</p>
                <div className="space-y-2">
                {items.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => handleSelectConversation(item.id)}
                className={`rounded-lg border px-3 py-2 text-left text-xs ${
                  conversationId === item.id
                    ? "border-blue-500 bg-blue-950/40"
                    : "border-slate-800 hover:bg-slate-900"
                }`}
              >
                <p className="truncate font-medium text-slate-200">
                  {item.title || `Conversación #${item.id}`}
                </p>

                <p className="mt-1 text-slate-500">
                  {item.createdAt ? new Date(item.createdAt).toLocaleString() : "Sin fecha"}                </p>
             <div className="mt-2 flex gap-2">
                  <button type="button" onClick={(e) => { e.stopPropagation(); handleRenameConversation(item.id, item.title); }} className="text-[11px] text-blue-400">Renombrar</button>
                  <button type="button" onClick={(e) => { e.stopPropagation(); handleExportConversation(item.id); }} className="text-[11px] text-emerald-400">Exportar</button>
                  <button type="button" onClick={(e) => { e.stopPropagation(); handleDeleteConversation(item.id); }} className="text-[11px] text-red-400">Eliminar</button>
                </div>
              </button>
              ))}
                </div>
              </div>
            ))}
          </div>
        </section>

        <section className="flex-1 overflow-y-auto">
          <div className="mb-3 flex items-center justify-between gap-2">
            <h2 className="text-sm font-semibold">Documentos</h2>

            <button
              type="button"
              onClick={clearDocumentFilter}
              className="text-xs text-blue-400 hover:text-blue-300"
            >
              Usar todos
            </button>
          </div>

          <label className="mb-3 block cursor-pointer rounded-lg border border-dashed border-slate-700 p-3 text-center text-xs text-slate-400 hover:bg-slate-900">
            {isUploading ? "Subiendo..." : "Subir PDF, TXT o MD"}
            <input
              type="file"
              accept=".pdf,.txt,.md"
              disabled={isUploading}
              onChange={handleUploadDocument}
              className="hidden"
            />
          </label>

          {documentError && (
            <p className="mb-3 rounded bg-red-950/50 p-2 text-xs text-red-300">
              {documentError}
            </p>
          )}

          {documentNotice && (
            <p className="mb-3 rounded bg-emerald-950/50 p-2 text-xs text-emerald-300">
              {documentNotice}
            </p>
          )}

          <div className="flex flex-col gap-2">
            {documents.length === 0 && (
              <p className="text-xs text-slate-500">
                Todavía no hay documentos cargados.
              </p>
            )}

            {documents.map((document) => {
              const isSelected = selectedDocumentIds.includes(document.id);

              return (
                <div
                  key={document.id}
                  className={`rounded-lg border p-3 text-xs ${
                    isSelected
                      ? "border-blue-500 bg-blue-950/40"
                      : "border-slate-800 bg-slate-900/50"
                  }`}
                >
                  <button
                    type="button"
                    onClick={() => toggleDocumentSelection(document.id)}
                    className="w-full text-left"
                  >
                    <p className="truncate font-medium text-slate-200">
                      {document.originalFileName}                    </p>

                    <p className="mt-1 text-slate-500">
                      {document.chunkCount} chunks
                    </p>
                  </button>

                  <button
                    type="button"
                    onClick={() => handleDeleteDocument(document.id)}
                    disabled={deletingDocumentId === document.id}
                    className="mt-2 text-xs text-red-400 hover:text-red-300 disabled:opacity-50"
                  >
                    {deletingDocumentId === document.id
                      ? "Eliminando..."
                      : "Eliminar"}
                  </button>
                </div>
              );
            })}
          </div>
        </section>

        <button
          type="button"
          onClick={handleLogout}
          className="mt-4 rounded-lg border border-slate-700 px-3 py-2 text-xs hover:bg-slate-800"
        >
          Cerrar sesión
        </button>
      </aside>

      <section className="flex min-w-0 flex-1 flex-col">
        <header className="border-b border-slate-800 p-4">
          <h1 className="text-lg font-semibold">LocalMind Chat</h1>

          <p className="text-sm text-slate-400">
            Consultá tus documentos o hacé preguntas generales.
          </p>
          <div className="mt-2 flex gap-3 text-xs">
            <a href="/metrics" className="text-blue-300 hover:text-blue-200">Ver métricas</a>
            <a href="/admin" className="text-blue-300 hover:text-blue-200">Ver admin</a>
          </div>

        </header>

        {chatError && (
          <div className="border-b border-red-900 bg-red-950/40 p-3 text-sm text-red-300">
            {chatError}
          </div>
        )}

        <section className="flex-1 space-y-4 overflow-y-auto p-4">
          {messages.length === 0 && (
            <div className="mx-auto mt-16 max-w-xl text-center text-slate-500">
              <p className="text-lg font-medium text-slate-300">
                Empezá una conversación
              </p>

              <p className="mt-2 text-sm">
                Podés consultar tus documentos cargados o hacer una pregunta
                general.
              </p>
            </div>
          )}

          {messages.map((chatMessage, index) => {
            const isUser = chatMessage.role === "user";
            const routeBadge = getRouteBadge(chatMessage);

            return (
              <article
                key={index}
                className={`max-w-3xl rounded-2xl p-4 ${
                  isUser
                    ? "ml-auto bg-blue-600 text-white"
                    : "mr-auto bg-slate-800 text-slate-100"
                }`}
              >
                <div className="whitespace-pre-wrap text-sm leading-relaxed">
                  {chatMessage.content}
                </div>

                {!isUser && routeBadge && (
                  <div className="mt-3 inline-flex rounded-full bg-slate-950/60 px-2 py-1 text-xs text-slate-300">
                    {routeBadge}
                  </div>
                )}

                {!isUser &&
                  chatMessage.sources &&
                  chatMessage.sources.length > 0 && (
                    <div className="mt-4 space-y-2">
                      <p className="text-xs font-semibold text-slate-300">
                        Fuentes usadas
                      </p>

                      {chatMessage.sources.map((source, sourceIndex) => (
                        <div
                          key={sourceIndex}
                          className="rounded-lg border border-slate-700 bg-slate-900/70 p-3 text-xs"
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div>
                              <p className="font-medium text-slate-200">
                                {source.fileName ?? "Documento sin nombre"}                              </p>

                              <p className="mt-1 text-slate-500">
                                Chunk #{source.chunkIndex}
                              </p>
                            </div>

                            <p className="font-semibold text-slate-200">
                              {typeof source.score === "number" ? source.score.toFixed(2) : "N/D"}                            </p>
                          </div>

                          <details className="mt-2">
                            <summary className="cursor-pointer text-slate-300 hover:text-white">
                              Ver preview del chunk usado
                            </summary>

                            <p className="mt-1 rounded bg-slate-950/70 p-2 text-slate-400">
                              {source.preview || "Sin preview disponible."}
                            </p>
                          </details>
                        </div>
                      ))}
                    </div>
                  )}
              </article>
            );
          })}

          {isStreaming && (
            <div className="mr-auto rounded-2xl bg-slate-800 p-3 text-slate-400">
              Generando respuesta...
            </div>
          )}
        </section>

        <form
          onSubmit={handleSend}
          className="flex flex-col gap-2 border-t border-slate-800 p-4"
        >
          <div className="text-xs text-slate-500">
            Consulta actual: RAG sobre {selectedDocumentsLabel}.
          </div>

          <div className="flex gap-2">
            <input
              value={message}
              onChange={(event) => setMessage(event.target.value)}
              placeholder="Preguntá sobre tus documentos..."
              disabled={isSending}
              className="flex-1 rounded-xl border border-slate-700 bg-slate-900 p-3 outline-none placeholder:text-slate-500 disabled:cursor-not-allowed disabled:opacity-60"
            />

            <button
              type="submit"
              disabled={!canSendMessage}
              className="rounded-xl bg-blue-600 px-5 py-3 font-semibold hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Enviar
            </button>

            <button
              type="button"
              onClick={() => streamAbortController?.abort()}
              disabled={!isStreaming}
              className="rounded-xl border border-slate-700 px-4 py-3 text-sm disabled:opacity-50"
            >
              Cancelar
            </button>
          </div>
        </form>
      </section>
    </main>
  );
}