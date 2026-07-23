import axios from "axios";
export const apiBaseUrl =  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5201/api";

export const api = axios.create({
  // baseURL: "http://localhost:5201/api",
  baseURL: apiBaseUrl,
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem("localmind_token");

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  return config;
});