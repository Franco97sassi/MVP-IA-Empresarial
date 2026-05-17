import { useEffect, useMemo, useState } from "react";
import axios from "axios";
import {
  getConversation,
  getHistory,
  sendMessage,
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

const TOOL_NAMES = [
  "calculator",
  "summarizeText",
  "extractTasks",
  "generateStudyPlan",
];
import { useEffect, useMemo, useState } from "react";
import {
  deleteDocument,
  getDocuments,
  getHistory,
  sendMessage,
  uploadDocument,
} from "./chatApi";

type ChatMessage = {
  role: "user" | "assistant";
  content: string;
  createdAt?: string;
  sources?: RagSource[];
  usedTool?: boolean;
  toolName?: string | null;
  route?: string | null;
};

type ConversationHistoryItem = {
  id: number;
  title: string;
  createdAt: string;
};

type DocumentItem = {
  id: number;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  status: string;
  createdAt: string;
  chunkCount: number;
};

type RagSource = {
  documentId: number;
  fileName: string;
  chunkIndex: number;
  chunkReference: string;
  preview: string;
  score: number;
  vectorScore: number;
  keywordScore: number;
  rankScore: number;
};

const ACCEPTED_FILE_EXTENSIONS = [".pdf", ".txt", ".md"];

function getRequestErrorMessage(error: unknown, fallback: string) {
  if (error instanceof Error) {
    return error.message;
  }

  return fallback;
}

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024) {
    return `${sizeBytes} B`;
  }

  if (sizeBytes < 1024 * 1024) {
    return `${(sizeBytes / 1024).toFixed(1)} KB`;
  }

  return `${(sizeBytes / 1024 / 1024).toFixed(1)} MB`;
}

function isAcceptedFile(file: File) {
  const fileName = file.name.toLowerCase();

  return ACCEPTED_FILE_EXTENSIONS.some((extension) =>
    fileName.endsWith(extension)
  );
}

function getDocumentsSubtitle(documentsCount: number) {
  if (documentsCount === 0) {
    return "PDF, TXT o MD para consultar";
  }

  return `${documentsCount} documento${documentsCount === 1 ? "" : "s"} indexado${
    documentsCount === 1 ? "" : "s"
  }`;
}

export default function ChatPage() {
  const [message, setMessage] = useState("");
  const [conversationId, setConversationId] = useState<number | null>(null);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [history, setHistory] = useState<ConversationHistoryItem[]>([]);
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [selectedDocumentIds, setSelectedDocumentIds] = useState<number[]>([]);

  const [isSending, setIsSending] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [deletingDocumentId, setDeletingDocumentId] = useState<number | null>(
    null
  );

  const [chatError, setChatError] = useState<string | null>(null);
  const [documentError, setDocumentError] = useState<string | null>(null);
  const [documentNotice, setDocumentNotice] = useState<string | null>(null);

  const canSendMessage = useMemo(() => {
    return message.trim().length > 0 && !isSending;
  }, [message, isSending]);

  const loadHistory = async () => {
    const data = await getHistory();
    setHistory(data);
  };

  const loadDocuments = async () => {
    const data = await getDocuments();
    setDocuments(data);
  };

  useEffect(() => {
    void loadHistory();
    void loadDocuments();
  }, []);

  const clearDocumentMessages = () => {
    setDocumentError(null);
    setDocumentNotice(null);
  };

  const handleUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];

    if (!file) {
      return;
    }

    event.target.value = "";

    if (!isAcceptedFile(file)) {
      setDocumentError("Solo se permiten archivos PDF, TXT o MD.");
      return;
    }

    clearDocumentMessages();
    setIsUploading(true);

    try {
      const uploadedDocument = await uploadDocument(file);

      setDocuments((currentDocuments) => [
        uploadedDocument,
        ...currentDocuments,
      ]);

      setDocumentNotice(`Documento "${uploadedDocument.originalFileName}" indexado.`);
    } catch (error) {
      console.error("Error subiendo documento:", error);
      setDocumentError(
        getRequestErrorMessage(error, "No se pudo subir el documento.")
      );
    } finally {
      setIsUploading(false);
    }
  };

  const handleDeleteDocument = async (document: DocumentItem) => {
    if (deletingDocumentId !== null) {
      return;
    }

    const confirmed = window.confirm(
      `¿Eliminar "${document.originalFileName}" y sus chunks del índice RAG?`
    );

    if (!confirmed) {
      return;
    }

    clearDocumentMessages();
    setDeletingDocumentId(document.id);

    try {
      await deleteDocument(document.id);

      setDocuments((currentDocuments) =>
        currentDocuments.filter((item) => item.id !== document.id)
      );

      setSelectedDocumentIds((currentIds) =>
        currentIds.filter((id) => id !== document.id)
      );

      setDocumentNotice(`Documento "${document.originalFileName}" eliminado.`);
    } catch (error) {
      console.error("Error eliminando documento:", error);
      setDocumentError(
        getRequestErrorMessage(error, "No se pudo eliminar el documento.")
      );
    } finally {
      setDeletingDocumentId(null);
    }
  };

  const toggleDocumentFilter = (documentId: number) => {
    setSelectedDocumentIds((currentIds) =>
      currentIds.includes(documentId)
        ? currentIds.filter((id) => id !== documentId)
        : [...currentIds, documentId]
    );
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
    setChatError(null);

    try {
      const data = await sendMessage({
        conversationId,
        message: userMessage,
        documentIds: selectedDocumentIds,
      });

      setConversationId(data.conversationId);

      setMessages((previousMessages) => [
        ...previousMessages,
        {
          role: "assistant",
          content: data.response,
          sources: data.sources,
          usedTool: data.usedTool,
          toolName: data.toolName,
          route: data.route,
        },
      ]);

      await loadHistory();
    } catch (error) {
      console.error("Error enviando mensaje:", error);

      const errorMessage = getRequestErrorMessage(
        error,
        "No se pudo procesar el mensaje."
      );

      setChatError(errorMessage);

      setMessages((previousMessages) => [
        ...previousMessages,
        {
          role: "assistant",
          content: errorMessage,
        },
      ]);
    } finally {
      setIsSending(false);
    }
  };

  return (
    <main className="flex h-screen bg-slate-950 text-slate-100">
      <aside className="w-80 border-r border-slate-800 p-4">
        <section className="mb-6">
          <h2 className="text-sm font-semibold">Conversaciones</h2>

          <div className="mt-3 space-y-2">
            {history.length === 0 ? (
              <p className="text-xs text-slate-500">
                Todavía no hay conversaciones.
              </p>
            ) : (
              history.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className="block w-full rounded-lg bg-slate-900 p-2 text-left text-xs hover:bg-slate-800"
                  onClick={() => setConversationId(item.id)}
                >
                  <p className="truncate font-medium">{item.title}</p>
                  <p className="text-slate-500">
                    {new Date(item.createdAt).toLocaleString()}
                  </p>
                </button>
              ))
            )}
          </div>
        </section>

        <section>
          <div className="mb-3 flex items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold">Documentos RAG</h2>
              <p className="text-xs text-slate-500">
                {getDocumentsSubtitle(documents.length)}
              </p>
            </div>

            <label className="cursor-pointer rounded bg-emerald-600 px-3 py-2 text-xs font-semibold hover:bg-emerald-700">
              {isUploading ? "Procesando..." : "Subir"}
              <input
                type="file"
                accept=".pdf,.txt,.md,text/plain,application/pdf,text/markdown"
                onChange={handleUpload}
                disabled={isUploading}
                className="hidden"
              />
            </label>
          </div>

          {documentError && (
            <p className="mb-2 rounded border border-red-500/40 bg-red-500/10 p-2 text-xs text-red-300">
              {documentError}
            </p>
          )}

          {documentNotice && (
            <p className="mb-2 rounded border border-emerald-500/40 bg-emerald-500/10 p-2 text-xs text-emerald-200">
              {documentNotice}
            </p>
          )}

          {documents.length > 0 && (
            <div className="mb-2 rounded-lg border border-blue-500/20 bg-blue-500/10 p-2 text-xs text-blue-100">
              Filtro RAG:{" "}
              {selectedDocumentIds.length === 0
                ? "todos los documentos"
                : `${selectedDocumentIds.length} seleccionado${
                    selectedDocumentIds.length === 1 ? "" : "s"
                  }`}
            </div>
          )}

          <div className="max-h-52 space-y-2 overflow-y-auto">
            {documents.length === 0 ? (
              <div className="rounded-lg border border-dashed border-slate-700 p-3 text-xs text-slate-500">
                <p>Todavía no subiste documentos.</p>
                <p className="mt-1">
                  Subí uno para activar respuestas con fuentes RAG.
                </p>
              </div>
            ) : (
              documents.map((document) => {
                const isDeleting = deletingDocumentId === document.id;

                return (
                  <article
                    key={document.id}
                    className="rounded-lg bg-slate-800 p-2 text-xs"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <label className="mt-0.5 flex items-center gap-2 text-[11px] text-slate-300">
                        <input
                          type="checkbox"
                          checked={selectedDocumentIds.includes(document.id)}
                          onChange={() => toggleDocumentFilter(document.id)}
                          className="accent-blue-500"
                        />
                        Usar
                      </label>

                      <div className="min-w-0 flex-1">
                        <p className="truncate font-medium">
                          {document.originalFileName}
                        </p>

                        <p className="text-slate-400">
                          {document.chunkCount} chunks ·{" "}
                          {formatFileSize(document.sizeBytes)}
                        </p>

                        <p className="text-slate-500">
                          Estado: {document.status}
                        </p>
                      </div>

                      <button
                        type="button"
                        onClick={() => handleDeleteDocument(document)}
                        disabled={isDeleting}
                        className="rounded border border-red-500/40 px-2 py-1 text-red-200 hover:bg-red-500/10 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        {isDeleting ? "..." : "Eliminar"}
                      </button>
                    </div>
                  </article>
                );
              })
            )}
          </div>
        </section>
      </aside>

      <section className="flex flex-1 flex-col">
        <header className="border-b border-slate-800 p-4">
          <h1 className="text-lg font-semibold">Chat LocalMind</h1>
          <p className="text-sm text-slate-400">
            Preguntá sobre tus documentos indexados.
          </p>
        </header>

        {chatError && (
          <p className="m-4 rounded border border-red-500/40 bg-red-500/10 p-3 text-sm text-red-300">
            {chatError}
          </p>
        )}

        <section className="flex-1 space-y-4 overflow-y-auto p-4">
          {messages.length === 0 && (
            <div className="mx-auto mt-20 max-w-xl text-center text-slate-500">
              <h2 className="text-xl font-semibold text-slate-300">
                Empezá una consulta
              </h2>
              <p className="mt-2 text-sm">
                Subí documentos y hacé preguntas para obtener respuestas con
                fuentes RAG.
              </p>
            </div>
          )}

          {messages.map((item, index) => {
            const isUserMessage = item.role === "user";

            return (
              <article
                key={`${item.role}-${item.createdAt ?? index}`}
                className={`max-w-3xl rounded-2xl p-3 whitespace-pre-wrap ${
                  isUserMessage ? "ml-auto bg-blue-600" : "mr-auto bg-slate-800"
                }`}
              >
                <p>{item.content}</p>

                {!isUserMessage && item.sources && item.sources.length > 0 && (
                  <div className="mt-3 space-y-2 border-t border-slate-700 pt-3">
                    <p className="text-xs font-semibold text-emerald-300">
                      Fuentes RAG usadas
                    </p>

                    {item.sources.map((source) => (
                      <div
                        key={`${source.documentId}-${source.chunkIndex}`}
                        className="rounded-lg bg-slate-900/60 p-2 text-xs text-slate-300"
                      >
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="font-medium text-slate-100">
                            {source.fileName} · {source.chunkReference}
                          </p>

                          <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-emerald-200">
                            rank {source.rankScore.toFixed(2)}
                          </span>
                        </div>

                        <div className="mt-1 flex flex-wrap gap-2 text-[11px] text-slate-400">
                          <span>vector {source.vectorScore.toFixed(2)}</span>
                          <span>keywords {source.keywordScore.toFixed(2)}</span>
                          <span>score final {source.score.toFixed(2)}</span>
                        </div>

                        <details className="mt-2">
                          <summary className="cursor-pointer text-slate-300 hover:text-white">
                            Ver preview del chunk usado
                          </summary>

                          <p className="mt-1 rounded bg-slate-950/70 p-2 text-slate-400">
                            {source.preview}
                          </p>
                        </details>
                      </div>
                    ))}
                  </div>
                )}
              </article>
            );
          })}

          {isSending && (
            <div className="mr-auto rounded-2xl bg-slate-800 p-3 text-slate-400">
              Pensando...
            </div>
          )}
        </section>

        <form
          onSubmit={handleSend}
          className="flex gap-2 border-t border-slate-800 p-4"
        >
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
        </form>
      </section>
    </main>
  );
}
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

  if (data && typeof data === "object") {
    const details = data as {
      detail?: unknown;
      message?: unknown;
      title?: unknown;
    };

    if (typeof details.detail === "string" && details.detail.trim().length > 0) {
      return details.detail;
    }

    if (typeof details.message === "string" && details.message.trim().length > 0) {
      return details.message;
    }

    if (typeof details.title === "string" && details.title.trim().length > 0) {
      return details.title;
    }
  }

  return fallback;
}

function formatFileSize(sizeBytes: number) {
  if (sizeBytes < 1024) {
    return `${sizeBytes} B`;
  }

  if (sizeBytes < 1024 * 1024) {
    return `${(sizeBytes / 1024).toFixed(1)} KB`;
  }

  return `${(sizeBytes / 1024 / 1024).toFixed(1)} MB`;
}

function isAcceptedFile(file: File) {
  const fileName = file.name.toLowerCase();

  return ACCEPTED_FILE_EXTENSIONS.some((extension) =>
    fileName.endsWith(extension)
  );
}

function getDocumentsSubtitle(documentsCount: number) {
  if (documentsCount === 0) {
    return "PDF, TXT o MD para consultar";
  }

  return `${documentsCount} documento${documentsCount === 1 ? "" : "s"} indexado${
    documentsCount === 1 ? "" : "s"
  }`;
}

export default function ChatPage() {
  const [message, setMessage] = useState("");
  const [conversationId, setConversationId] = useState<number | null>(null);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [history, setHistory] = useState<ConversationHistoryItem[]>([]);
  const [documents, setDocuments] = useState<DocumentItem[]>([]);

  const [isSending, setIsSending] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [deletingDocumentId, setDeletingDocumentId] = useState<number | null>(
    null
  );

  const [chatError, setChatError] = useState<string | null>(null);
  const [documentError, setDocumentError] = useState<string | null>(null);
  const [documentNotice, setDocumentNotice] = useState<string | null>(null);

  const canSendMessage = useMemo(() => {
    return message.trim().length > 0 && !isSending;
  }, [message, isSending]);

  const loadHistory = async () => {
    const data = await getHistory();
    setHistory(data);
  };

  const loadDocuments = async () => {
    const data = await getDocuments();
    setDocuments(data);
  };

  const loadInitialData = async () => {
    const [historyData, documentsData] = await Promise.all([
      getHistory(),
      getDocuments(),
    ]);

    setHistory(historyData);
    setDocuments(documentsData);
  };

  useEffect(() => {
    loadInitialData().catch((error) => {
      console.error("Error cargando datos iniciales:", error);
      setChatError("No se pudieron cargar el historial y los documentos.");
    });
  }, []);

  const clearDocumentMessages = () => {
    setDocumentError(null);
    setDocumentNotice(null);
  };

  const handleNewConversation = () => {
    setConversationId(null);
    setMessages([]);
    setChatError(null);
  };

  const handleOpenConversation = async (selectedConversationId: number) => {
    setChatError(null);

    try {
      const data = await getConversation(selectedConversationId);

      setConversationId(data.id);
      setMessages(
        data.messages.map((item) => ({
          role: item.role,
          content: item.content,
          createdAt: item.createdAt,
        }))
      );
    } catch (error) {
      console.error("Error abriendo conversación:", error);
      setChatError("No se pudo abrir la conversación seleccionada.");
    }
  };

  const handleUpload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";

    if (!file || isUploading) {
      return;
    }

    clearDocumentMessages();

    if (!isAcceptedFile(file)) {
      setDocumentError("Formato inválido. Subí un archivo PDF, TXT o MD.");
      return;
    }

    if (file.size > MAX_FILE_SIZE_BYTES) {
      setDocumentError("El archivo supera el límite de 10 MB.");
      return;
    }

    setIsUploading(true);

    try {
      const uploadedDocument = await uploadDocument(file);

      await loadDocuments();

      setDocumentNotice(
        `Documento "${uploadedDocument.originalFileName}" procesado con ${uploadedDocument.chunkCount} chunks.`
      );
    } catch (error) {
      console.error("Error subiendo documento:", error);
      setDocumentError(
        getRequestErrorMessage(error, "No se pudo procesar el documento.")
      );
    } finally {
      setIsUploading(false);
    }
  };

  const handleDeleteDocument = async (document: DocumentItem) => {
    if (deletingDocumentId !== null) {
      return;
    }

    const confirmed = window.confirm(
      `¿Eliminar "${document.originalFileName}" y sus chunks del índice RAG?`
    );

    if (!confirmed) {
      return;
    }

    clearDocumentMessages();
    setDeletingDocumentId(document.id);

    try {
      await deleteDocument(document.id);

      setDocuments((currentDocuments) =>
        currentDocuments.filter((item) => item.id !== document.id)
      );

      setDocumentNotice(`Documento "${document.originalFileName}" eliminado.`);
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
    setChatError(null);

    try {
      const data = await sendMessage({
        conversationId,
        message: userMessage,
      });

      setConversationId(data.conversationId);

      setMessages((previousMessages) => [
        ...previousMessages,
        {
          role: "assistant",
          content: data.response,
          sources: data.sources,
          usedTool: data.usedTool,
          toolName: data.toolName,
          route: data.route,
        },
      ]);

      await loadHistory();
    } catch (error) {
      console.error("Error enviando mensaje:", error);

      const errorMessage = getRequestErrorMessage(
        error,
        "No se pudo procesar el mensaje."
      );

      setChatError(errorMessage);
      setMessages((previousMessages) => [
        ...previousMessages,
        {
          role: "assistant",
          content: errorMessage,
        },
      ]);
    } finally {
      setIsSending(false);
    }
  };

  const handleLogout = () => {
    localStorage.removeItem("localmind_token");
    localStorage.removeItem("localmind_email");
    window.location.href = "/login";
  };

  return (
    <div className="flex h-screen bg-slate-950 text-white">
      <aside className="flex w-80 flex-col gap-4 border-r border-slate-800 bg-slate-900 p-4">
        <header>
          <h1 className="text-xl font-bold">LocalMind AI</h1>
          <p className="text-xs text-slate-400">
            Chat local con documentos, RAG y tools
          </p>
        </header>

        <button
          type="button"
          onClick={handleNewConversation}
          className="rounded bg-blue-600 p-2 font-medium hover:bg-blue-700"
        >
          Nueva conversación
        </button>

        <section className="rounded-xl border border-slate-800 bg-slate-950/60 p-3">
          <div className="mb-3 flex items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold">Documentos</h2>
              <p className="text-xs text-slate-500">
                {getDocumentsSubtitle(documents.length)}
              </p>
            </div>

            <label className="cursor-pointer rounded bg-emerald-600 px-3 py-2 text-xs font-semibold hover:bg-emerald-700">
              {isUploading ? "Procesando..." : "Subir"}
              <input
                type="file"
                accept=".pdf,.txt,.md,text/plain,application/pdf,text/markdown"
                onChange={handleUpload}
                disabled={isUploading}
                className="hidden"
              />
            </label>
          </div>

          {documentError && (
            <p className="mb-2 rounded border border-red-500/40 bg-red-500/10 p-2 text-xs text-red-300">
              {documentError}
            </p>
          )}

          {documentNotice && (
            <p className="mb-2 rounded border border-emerald-500/40 bg-emerald-500/10 p-2 text-xs text-emerald-200">
              {documentNotice}
            </p>
          )}

          <div className="max-h-52 space-y-2 overflow-y-auto">
            {documents.length === 0 ? (
              <div className="rounded-lg border border-dashed border-slate-700 p-3 text-xs text-slate-500">
                <p>Todavía no subiste documentos.</p>
                <p className="mt-1">
                  Subí uno para activar respuestas con fuentes RAG.
                </p>
              </div>
            ) : (
              documents.map((document) => {
                const isDeleting = deletingDocumentId === document.id;

                return (
                  <article
                    key={document.id}
                    className="rounded-lg bg-slate-800 p-2 text-xs"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="truncate font-medium">
                          {document.originalFileName}
                        </p>

                        <p className="text-slate-400">
                          {document.chunkCount} chunks ·{" "}
                          {formatFileSize(document.sizeBytes)}
                        </p>

                        <p className="text-slate-500">
                          Estado: {document.status}
                        </p>
                      </div>

                      <button
                        type="button"
                        onClick={() => handleDeleteDocument(document)}
                        disabled={isDeleting}
                        className="rounded border border-red-500/40 px-2 py-1 text-red-200 hover:bg-red-500/10 disabled:cursor-not-allowed disabled:opacity-60"
                      >
                        {isDeleting ? "..." : "Eliminar"}
                      </button>
                    </div>
                  </article>
                );
              })
            )}
          </div>
        </section>

        <section className="rounded-xl border border-slate-800 bg-slate-950/60 p-3 text-xs text-slate-400">
          <h2 className="mb-2 text-sm font-semibold text-slate-200">Tools</h2>

          <div className="flex flex-wrap gap-2">
            {TOOL_NAMES.map((tool) => (
              <span
                key={tool}
                className="rounded-full bg-indigo-500/10 px-2 py-1 text-indigo-200"
              >
                {tool}
              </span>
            ))}
          </div>
        </section>

        <section className="flex-1 space-y-2 overflow-y-auto">
          <h2 className="text-sm font-semibold text-slate-300">Historial</h2>

          {history.length === 0 ? (
            <p className="text-xs text-slate-500">
              Aún no hay conversaciones.
            </p>
          ) : (
            history.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => handleOpenConversation(item.id)}
                className={`w-full cursor-pointer rounded p-2 text-left text-sm hover:bg-slate-700 ${
                  item.id === conversationId ? "bg-slate-700" : "bg-slate-800"
                }`}
              >
                <span className="line-clamp-2">{item.title}</span>
              </button>
            ))
          )}
        </section>

        <button
          type="button"
          onClick={handleLogout}
          className="text-sm text-red-400 hover:text-red-300"
        >
          Cerrar sesión
        </button>
      </aside>

      <main className="flex flex-1 flex-col">
        <section className="flex-1 space-y-4 overflow-y-auto p-6">
          {chatError && (
            <div className="rounded-xl border border-red-500/40 bg-red-500/10 p-3 text-sm text-red-200">
              {chatError}
            </div>
          )}

          {messages.length === 0 && (
            <div className="mt-24 text-center text-slate-500">
              <p className="text-lg font-medium text-slate-400">
                Empezá una conversación
              </p>
              <p className="mt-2 text-sm">
                Subí documentos y preguntá sobre ellos, o iniciá un chat
                general.
              </p>
            </div>
          )}

          {messages.map((item, index) => {
            const isUserMessage = item.role === "user";

            return (
              <article
                key={`${item.role}-${item.createdAt ?? index}`}
                className={`max-w-3xl rounded-2xl p-3 whitespace-pre-wrap ${
                  isUserMessage ? "ml-auto bg-blue-600" : "mr-auto bg-slate-800"
                }`}
              >
                <p>{item.content}</p>

                {!isUserMessage && item.sources && item.sources.length > 0 && (
                  <div className="mt-3 space-y-2 border-t border-slate-700 pt-3">
                    <p className="text-xs font-semibold text-emerald-300">
                      Fuentes RAG usadas
                    </p>

                    {item.sources.map((source) => (
                      <div
                        key={`${source.documentId}-${source.chunkIndex}`}
                        className="rounded-lg bg-slate-900/60 p-2 text-xs text-slate-300"
                      >
                        <p className="font-medium">
                          {source.fileName} · chunk {source.chunkIndex} · score{" "}
                          {source.score.toFixed(2)}
                        </p>

                        <p className="mt-1 text-slate-400">
                          {source.preview}
                        </p>
                      </div>
                    ))}
                  </div>
                )}
              </article>
            );
          })}

          {isSending && (
            <div className="mr-auto rounded-2xl bg-slate-800 p-3 text-slate-400">
              Pensando...
            </div>
          )}
        </section>

        <form
          onSubmit={handleSend}
          className="flex gap-2 border-t border-slate-800 p-4"
        >
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
            className="rounded-xl bg-blue-600 px-6 font-semibold hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Enviar
          </button>
        </form>
      </main>
    </div>
  );
}