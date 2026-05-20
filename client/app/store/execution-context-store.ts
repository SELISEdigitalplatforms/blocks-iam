import { create } from "zustand";

type ExecutionContext = {
  tenantId: string;
  contextId: string; // impersonation / execution context token from backend
};

export interface IExecutionContextStore {
  context: ExecutionContext | null;
  setContext: (context: ExecutionContext) => void;
  resetContext: () => void;
  reset: () => void;
}

export const useExecutionContextStore = create<IExecutionContextStore>()(
  (set) => ({
    context: null,
    setContext(context) {
      set((state) => ({ ...state, context }));
    },
    resetContext() {
      set((state) => ({ ...state, context: null }));
    },
    reset() {
      set(() => ({ context: null }));
    },
  }),
);
