import { create } from "zustand";

interface ConnectionState {
  sidecarPort: number | null;
  sidecarReady: boolean;
  setSidecarPort: (port: number) => void;
  setSidecarReady: (ready: boolean) => void;
}

export const useConnectionStore = create<ConnectionState>((set) => ({
  sidecarPort: null,
  sidecarReady: false,
  setSidecarPort: (port) => set({ sidecarPort: port }),
  setSidecarReady: (ready) => set({ sidecarReady: ready }),
}));
