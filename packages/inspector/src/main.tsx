import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { GuaInspectorApp } from "./App";
import "./styles.css";

const root = document.getElementById("root");

if (root === null) {
  throw new Error("Gua Inspector root element was not found.");
}

createRoot(root).render(
  <StrictMode>
    <GuaInspectorApp />
  </StrictMode>,
);
