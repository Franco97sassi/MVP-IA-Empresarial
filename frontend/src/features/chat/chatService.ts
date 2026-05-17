import { api } from "../../services/api";

export interface ConversationResponse {
  id: number;
  title: string;
  messages: ChatMessage[];
}

export const getConversation = async (
  conversationId: number
): Promise<ConversationResponse> => {
  const response = await api.get<ConversationResponse>(
    `/chat/history/${conversationId}`
  );

  return response.data;
};
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

export const sendMessage = async (
  data: SendMessageRequest
): Promise<SendMessageResponse> => {
  const response = await api.post<SendMessageResponse>("/chat/send", data);
  return response.data;
};

export const getHistory = async (): Promise<ConversationHistoryItem[]> => {
  const response = await api.get<ConversationHistoryItem[]>("/chat/history");
  return response.data;
};