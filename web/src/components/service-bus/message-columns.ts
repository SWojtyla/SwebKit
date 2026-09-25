import { formatLocalTime } from "@/lib/datetime";
import type { RowDensity } from "@/lib/stores/sb-preferences";
import type { SbMessage } from "@/lib/types";

export const densityClass: Record<RowDensity, string> = {
    compact: "py-0.5",
    default: "py-1.5",
    comfort: "py-2.5",
};

// Estimated row height per density, used as the virtualizer's initial size
// guess before `measureElement` corrects it against the real rendered height.
export const ROW_HEIGHT_ESTIMATE: Record<RowDensity, number> = {
    compact: 24,
    default: 32,
    comfort: 40,
};

// The message list renders as a real data table (see ColumnDef below), but
// virtualized rows are absolutely-positioned siblings rather than actual
// <tr> elements in a shared <table> — so every row (and the header) must
// share one explicit `grid-template-columns` string for the columns to line
// up. These widths mirror the `max-w-[...]` classes the columns already had.
export const CHECKBOX_COL_WIDTH = "32px";
export const CUSTOM_COLUMN_WIDTH = "140px";
export const COLUMN_WIDTHS: Record<string, string> = {
    enqueuedAt: "110px",
    sequenceNumber: "90px",
    messageId: "160px",
    correlationId: "140px",
    subject: "220px",
    deliveryCount: "90px",
    contentType: "120px",
    sessionId: "120px",
    partitionKey: "130px",
    deadLetterReason: "200px",
    nsbEndpoint: "200px",
    nsbMessageType: "160px",
    nsbTimeSent: "150px",
    nsbConversation: "200px",
};

// Real columns rendered as an actual data table — this is what the MAUI
// desktop app's message grid looked like (a dense spreadsheet-style view,
// each field in its own column) rather than a stacked card per message.
// Order matches MAUI's default column order; `dlqOnly` columns only render
// in the DLQ view, same as before.
export interface ColumnDef {
    key: string;
    label: string;
    className?: string;
    dlqOnly?: boolean;
    render: (msg: SbMessage) => string;
}

export const COLUMN_DEFS: ColumnDef[] = [
    {
        key: "enqueuedAt",
        label: "Enqueued",
        render: (m) => formatLocalTime(m.enqueuedAt),
    },
    {
        key: "sequenceNumber",
        label: "Seq #",
        render: (m) =>
            m.sequenceNumber !== null ? `#${m.sequenceNumber}` : "-",
    },
    {
        key: "messageId",
        label: "Message ID",
        className: "max-w-[160px]",
        render: (m) => m.messageId,
    },
    {
        key: "correlationId",
        label: "Correlation ID",
        className: "max-w-[140px]",
        render: (m) => m.correlationId ?? "-",
    },
    {
        key: "subject",
        label: "Subject",
        className: "max-w-[220px]",
        render: (m) => m.subject ?? "-",
    },
    {
        key: "deliveryCount",
        label: "Delivery",
        render: (m) => String(m.deliveryCount),
    },
    {
        key: "contentType",
        label: "Content Type",
        render: (m) => m.contentType ?? "-",
    },
    { key: "sessionId", label: "Session", render: (m) => m.sessionId ?? "-" },
    {
        key: "partitionKey",
        label: "Partition Key",
        render: (m) => m.systemProperties?.partitionKey ?? "-",
    },
    {
        key: "deadLetterReason",
        label: "DLQ Reason",
        className: "max-w-[200px]",
        dlqOnly: true,
        render: (m) => m.deadLetterReason ?? "-",
    },
];

export const NSB_COLUMN_DEFS: ColumnDef[] = [
    {
        key: "nsbEndpoint",
        label: "NSB Endpoint",
        className: "max-w-[200px]",
        render: (m) => getNsbProp(m, "NServiceBus.OriginatingEndpoint"),
    },
    {
        key: "nsbMessageType",
        label: "NSB Message Type",
        render: (m) =>
            truncateNsbType(getNsbProp(m, "NServiceBus.EnclosedMessageTypes")),
    },
    {
        key: "nsbTimeSent",
        label: "NSB Time Sent",
        className: "max-w-[150px]",
        render: (m) => getNsbProp(m, "NServiceBus.TimeSent"),
    },
    {
        key: "nsbConversation",
        label: "NSB Conversation",
        className: "max-w-[200px]",
        render: (m) => getNsbProp(m, "NServiceBus.ConversationId"),
    },
];

function getNsbProp(message: SbMessage, key: string): string {
    const value = message.applicationProperties[key];
    return value === undefined || value === null ? "-" : String(value);
}

function truncateNsbType(value: string): string {
    if (value === "-") return value;
    const comma = value.indexOf(",");
    const typePart = comma > 0 ? value.slice(0, comma) : value;
    const lastDot = typePart.lastIndexOf(".");
    return lastDot >= 0 ? typePart.slice(lastDot + 1) : typePart;
}
