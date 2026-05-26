import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getAdminDashboard, getRecentAudit, getUsersUsage, type AdminDashboard, type AuditLog, type UserUsage } from "./adminService";

const emptyDashboard: AdminDashboard = { users: 0, documents: 0, storageBytes: 0, requests24h: 0 };

export default function AdminPage() {
  const [dashboard, setDashboard] = useState(emptyDashboard);
  const [usersUsage, setUsersUsage] = useState<UserUsage[]>([]);
  const [audit, setAudit] = useState<AuditLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([getAdminDashboard(), getUsersUsage(), getRecentAudit(50)])
      .then(([d, u, a]) => { setDashboard(d); setUsersUsage(u); setAudit(a); })
      .catch(() => setError("No se pudieron cargar los endpoints de admin (requiere rol Admin)."))
      .finally(() => setLoading(false));
  }, []);

  return <div className="min-h-screen bg-slate-950 p-6 text-white"><div className="mx-auto max-w-6xl space-y-6"><header className="flex items-center justify-between rounded-2xl border border-slate-800 bg-slate-900 p-6"><h1 className="text-2xl font-bold">Panel Admin</h1><Link to="/chat" className="rounded-xl bg-blue-600 px-4 py-2 font-semibold">Volver</Link></header>{error && <div className="rounded-xl bg-red-900/30 p-4 text-red-200">{error}</div>}{loading ? <div className="rounded-xl border border-slate-800 bg-slate-900 p-4">Cargando...</div> : <><section className="grid gap-4 md:grid-cols-4"><article className="rounded-xl border border-slate-800 bg-slate-900 p-4"><p>Usuarios</p><p className="text-2xl">{dashboard.users}</p></article><article className="rounded-xl border border-slate-800 bg-slate-900 p-4"><p>Documentos</p><p className="text-2xl">{dashboard.documents}</p></article><article className="rounded-xl border border-slate-800 bg-slate-900 p-4"><p>Storage</p><p className="text-2xl">{dashboard.storageBytes}</p></article><article className="rounded-xl border border-slate-800 bg-slate-900 p-4"><p>Requests 24h</p><p className="text-2xl">{dashboard.requests24h}</p></article></section><section className="rounded-xl border border-slate-800 bg-slate-900 p-4"><h2 className="mb-3 text-lg">Uso por usuario</h2><div className="space-y-2 text-sm">{usersUsage.map((item) => <div key={item.id} className="flex justify-between rounded bg-slate-950 p-2"><span>{item.email}</span><span>docs: {item.documents} | bytes: {item.storageBytes}</span></div>)}</div></section><section className="rounded-xl border border-slate-800 bg-slate-900 p-4"><h2 className="mb-3 text-lg">Auditoría reciente</h2><div className="space-y-2 text-xs">{audit.map((entry) => <div key={entry.id} className="rounded bg-slate-950 p-2">{new Date(entry.createdAt).toLocaleString()} · {entry.method} {entry.path} · {entry.statusCode}</div>)}</div></section></>}</div></div>;
}
