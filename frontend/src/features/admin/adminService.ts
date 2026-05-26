import { api } from "../../services/api";

export interface AdminDashboard { users: number; documents: number; storageBytes: number; requests24h: number; }
export interface UserUsage { id: number; email: string; documents: number; storageBytes: number; }
export interface AuditLog { id: number; method: string; path: string; statusCode: number; createdAt: string; }

export const getAdminDashboard = async (): Promise<AdminDashboard> => (await api.get<AdminDashboard>("/admin/dashboard")).data;
export const getUsersUsage = async (): Promise<UserUsage[]> => (await api.get<UserUsage[]>("/admin/users/usage")).data;
export const getRecentAudit = async (take = 100): Promise<AuditLog[]> => (await api.get<AuditLog[]>(`/admin/audit/recent?take=${take}`)).data;
