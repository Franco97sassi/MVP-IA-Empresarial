import { api } from "../../services/api";
import { parseSseBuffer } from "./chatSseParser";
export interface ConversationResponse {
  id: number;
  title: string;
  messages: ChatMessage[];
}

export interface RagSource {
  documentId: number;
  fileName: string;
  chunkIndex: number;
  score: number;
  vectorScore: number;
  keywordScore: number;
  rankScore: number;
  preview: string;
  chunkReference: string;
}

export interface ChatMessage {
  role: "user" | "assistant";
  content: string;
  createdAt?: string;
  sources?: RagSource[];
  usedTool?: boolean;
  toolName?: string | null;
  route?: string;
}

export interface SendMessageRequest {
  conversationId: number | null;
  message: string;
  documentIds: number[];
}

export interface SendMessageResponse {
  conversationId: number;
  response: string;
  usedRag: boolean;
  usedTool: boolean;
  toolName: string | null;
  route: string;
  chunksUsed: number;
  sources: RagSource[];
}

export interface ConversationHistoryItem {
  id: number;
  title: string;
  createdAt: string;
}

export const getConversation = async (
  conversationId: number
): Promise<ConversationResponse> => {
  const response = await api.get<ConversationResponse>(
    `/chat/history/${conversationId}`
  );

  return response.data;
};

export const getHistory = async (): Promise<ConversationHistoryItem[]> => {
  const response = await api.get<ConversationHistoryItem[]>("/chat/history");
  return response.data;
};

export const renameConversation = async (
  conversationId: number,
  title: string
): Promise<void> => {
  await api.put(`/chat/history/${conversationId}/title`, { title });
};

export const deleteConversation = async (
  conversationId: number
): Promise<void> => {
  await api.delete(`/chat/history/${conversationId}`);
};

export const exportConversation = async (
  conversationId: number
): Promise<string> => {
  const response = await api.get<string>(
    `/chat/history/${conversationId}/export`,
    { responseType: "text" as const }
  );

  return response.data;
};

export interface StreamHandlers {
  onMeta: (conversationId: number) => void;
  onChunk: (chunk: string) => void;
  onDone: (payload: SendMessageResponse) => void;
}

export const sendMessageStream = async (
  data: SendMessageRequest,
  token: string,
  handlers: StreamHandlers,
  signal?: AbortSignal
) => {
  const response = await fetch(
    `${import.meta.env.VITE_API_URL}/chat/send-stream`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(data),
      signal,
    }
  );

  if (!response.ok || !response.body) {
    throw new Error("No se pudo iniciar el streaming.");
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();

  let buffer = "";
  let currentEvent = "";

  while (true) {
    const { value, done } = await reader.read();

    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    const { events, rest } = parseSseBuffer(buffer);
    buffer = rest;

    for (const parsed of events) {
      currentEvent = parsed.event;
      const dataLine = parsed.data;

      if (currentEvent === "meta") {
        handlers.onMeta(JSON.parse(dataLine).conversationId);
      }

      if (currentEvent === "chunk") {
        handlers.onChunk(dataLine);
      }

      if (currentEvent === "done") {
        handlers.onDone(JSON.parse(dataLine) as SendMessageResponse);
      }
    }
  }
};