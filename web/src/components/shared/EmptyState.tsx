import type { ReactNode } from "react";
import type { LucideIcon } from "lucide-react";

interface EmptyStateProps {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: ReactNode;
  testId?: string;
}

/**
 * Shared empty-state placeholder. Replaces the one-line "No X found" text that
 * was hand-rolled per feature (Redis/Storage/Monitoring/etc.) with an inconsistent
 * look each time.
 */
export function EmptyState({ icon: Icon, title, description, action, testId }: EmptyStateProps) {
  return (
    <div
      className="flex flex-col items-center justify-center gap-2 p-8 text-center"
      data-testid={testId ?? "empty-state"}
    >
      {Icon && <Icon className="h-8 w-8 text-muted-foreground/50" />}
      <p className="text-sm font-medium text-muted-foreground">{title}</p>
      {description && <p className="max-w-sm text-xs text-muted-foreground/80">{description}</p>}
      {action && <div className="mt-1">{action}</div>}
    </div>
  );
}
