import { create } from "zustand";

interface SelectionState {
  activeProject: string | null;
  activeEnvironment: string | null;
  setActiveProject: (project: string | null) => void;
  setActiveEnvironment: (env: string | null) => void;
}

export const useSelectionStore = create<SelectionState>((set) => ({
  activeProject: null,
  activeEnvironment: null,
  setActiveProject: (project) => set({ activeProject: project }),
  setActiveEnvironment: (env) => set({ activeEnvironment: env }),
}));
