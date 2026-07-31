import { useMutation, useQueryClient, type UseMutationResult } from "@tanstack/react-query";
import { useNotification } from "@/components/layout/NotificationSystem";

export interface NotifyMutationOptions<TData, TVariables> {
  mutationFn: (vars: TVariables) => Promise<TData>;
  successMessage: string | ((data: TData, vars: TVariables) => string);
  errorPrefix: string;
  invalidateKeys?: string[][];
}

export function useNotifyMutation<TData = unknown, TVariables = void>(
  options: NotifyMutationOptions<TData, TVariables>,
): UseMutationResult<TData, Error, TVariables> {
  const { notify } = useNotification();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: options.mutationFn,
    onSuccess: (data, vars) => {
      const message =
        typeof options.successMessage === "function"
          ? options.successMessage(data, vars)
          : options.successMessage;
      notify("success", message);
      if (options.invalidateKeys) {
        for (const key of options.invalidateKeys) {
          queryClient.invalidateQueries({ queryKey: key });
        }
      }
    },
    onError: (error) => {
      notify("error", options.errorPrefix, String(error));
    },
  });
}
