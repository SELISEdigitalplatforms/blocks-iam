import { createBrowserRouter, Navigate } from "react-router";
import { AppLayout } from "./components/AppLayout";
import { HomePage } from "./pages/HomePage";
import { EventsPage } from "./pages/EventsPage";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: "events", element: <EventsPage /> },
      { path: "api/*", element: <Navigate to="/" replace /> },
    ],
  },
]);
