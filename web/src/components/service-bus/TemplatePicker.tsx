import type { SbMessageTemplate } from "@/lib/types";
import { TemplateManager } from "./TemplateManager";

interface Props {
  onSelect: (template: SbMessageTemplate) => void;
  onClose: () => void;
}

/**
 * Pick-mode wrapper over the shared template manager: same searchable list the
 * composer has always had, now backed by the full management surface. Keeps the
 * `template-picker`/`template-select-*`/`template-delete-*` testids intact.
 */
export function TemplatePicker({ onSelect, onClose }: Props) {
  return <TemplateManager onPick={onSelect} onClose={onClose} />;
}
