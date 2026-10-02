import {
    useMutation,
    useQueryClient,
    type UseMutationResult,
} from "@tanstack/react-query";
import { useNotification } from "@/components/layout/notification-context";
import { describeApiError } from "@/lib/api/transport";

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
            // describeApiError appends the classified detail/hint the sidecar sends, so
            // the toast says *why* it failed, not just "Error: Internal server error".
            notify("error", options.errorPrefix, describeApiError(error));
        },
    });
}
