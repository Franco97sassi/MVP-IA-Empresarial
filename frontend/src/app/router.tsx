import { createBrowserRouter, Navigate } from "react-router-dom";
import LoginPage from "../features/auth/LoginPage";
import RegisterPage from "../features/auth/RegisterPage";
import ChatPage from "../features/chat/ChatPage";
import ProtectedRoute from "../components/ProtectedRoute";
import MetricsPage from "../features/metrics/MetricsPage";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <Navigate to="/chat" replace />,
  },
  {
    path: "/login",
    element: <LoginPage />,
  },
  {
    path: "/register",
    element: <RegisterPage />,
  },
  {
    path: "/chat",
    element: (
      <ProtectedRoute>
        <ChatPage />
      </ProtectedRoute>
    ),
  },
    {
    path: "/metrics",
    element: (
      <ProtectedRoute>
        <MetricsPage />
      </ProtectedRoute>
    ),
  },
  {
    path: "/admin",
    element: (
      <ProtectedRoute>
        <AdminPage />
      </ProtectedRoute>
    ),
  },
]);