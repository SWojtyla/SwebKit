import {
    ResizablePanel,
    type ResizablePanelProps,
} from "@/components/ui/ResizablePanel";

export type SidePanelProps = ResizablePanelProps;

export function SidePanel({
    "data-testid": panelTestId = "side-panel",
    closeTestId = "side-panel-close",
    className,
    ...props
}: SidePanelProps) {
    return (
        <ResizablePanel
            data-testid={panelTestId}
            closeTestId={closeTestId}
            // SwebOS: the detail rail sits on the raised surface; bg-card already
            // bridges there, this just keeps the panel on the semantic token.
            className={`bg-surface-1 ${className ?? ""}`}
            {...props}
        />
    );
}
