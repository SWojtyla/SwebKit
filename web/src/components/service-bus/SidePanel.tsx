import { ResizablePanel, type ResizablePanelProps } from "@/components/ui/ResizablePanel";

export type SidePanelProps = ResizablePanelProps;

export function SidePanel({
  "data-testid": panelTestId = "side-panel",
  closeTestId = "side-panel-close",
  ...props
}: SidePanelProps) {
  return (
    <ResizablePanel
      data-testid={panelTestId}
      closeTestId={closeTestId}
      {...props}
    />
  );
}
