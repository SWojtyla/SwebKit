import { describe, expect, it } from "vitest";
import {
    accessStatusLabel,
    buildAccessRequestText,
    capabilityDisplayName,
    featureAreaLabel,
    principalDisplay,
} from "./access-format";
import type { AccessRequestArtifact } from "./types";

function artifact(overrides: Partial<AccessRequestArtifact> = {}): AccessRequestArtifact {
    return {
        role: "Azure Service Bus Data Receiver",
        scope: "/subscriptions/s/resourceGroups/rg/providers/Microsoft.ServiceBus/namespaces/ns",
        principal: {
            objectId: "obj-1",
            upn: "dev@contoso.com",
            tenantId: "t-1",
            appId: null,
            displayName: "Dev User",
        },
        resource: "orders",
        summaryText: "Access request: ...",
        azCommand: "az role assignment create --assignee-object-id obj-1 ...",
        grantStatement: null,
        remedyKind: "ArmRole",
        assignmentKind: "permanent",
        webhookConfigured: false,
        ...overrides,
    };
}

describe("capabilityDisplayName", () => {
    it("maps known capabilities to friendly labels", () => {
        expect(capabilityDisplayName("servicebus.send")).toBe("Send messages");
        expect(capabilityDisplayName("sql.metadata")).toBe("Browse schema");
    });

    it("falls back to the raw key for unknown capabilities", () => {
        expect(capabilityDisplayName("new.area.cap")).toBe("new.area.cap");
    });
});

describe("featureAreaLabel", () => {
    it("maps known areas", () => {
        expect(featureAreaLabel("ServiceBus")).toBe("Service Bus");
    });

    it("falls back to the raw key", () => {
        expect(featureAreaLabel("Whatever")).toBe("Whatever");
    });
});

describe("accessStatusLabel", () => {
    it("labels all three statuses", () => {
        expect(accessStatusLabel("Ok")).toBe("OK");
        expect(accessStatusLabel("Denied")).toBe("Denied");
        expect(accessStatusLabel("Unknown")).toBe("Unknown");
    });
});

describe("principalDisplay", () => {
    it("prefers the UPN", () => {
        expect(
            principalDisplay({
                objectId: "o",
                upn: "u@x.com",
                tenantId: null,
                appId: null,
                displayName: null,
            }),
        ).toBe("u@x.com");
    });

    it("degrades to object id + app id for service principals", () => {
        expect(
            principalDisplay({
                objectId: "o-1",
                upn: null,
                tenantId: null,
                appId: "app-9",
                displayName: null,
            }),
        ).toBe("o-1 (app app-9)");
    });

    it("is honest when unresolved", () => {
        expect(principalDisplay(null)).toContain("unresolved");
    });
});

describe("buildAccessRequestText", () => {
    it("includes role, resource, scope, principal and the az command", () => {
        const text = buildAccessRequestText(artifact());
        expect(text).toContain("Azure Service Bus Data Receiver");
        expect(text).toContain("Resource: orders");
        expect(text).toContain("Scope: /subscriptions/s/");
        expect(text).toContain("Principal: dev@contoso.com");
        expect(text).toContain("az role assignment create");
    });

    it("says to ask an admin when scope is unknown instead of dropping the line", () => {
        const text = buildAccessRequestText(
            artifact({ scope: null, azCommand: null }),
        );
        expect(text).toContain("ask your admin");
        expect(text).not.toContain("az role assignment");
    });

    it("includes the grant statement for SQL remedies", () => {
        const text = buildAccessRequestText(
            artifact({
                remedyKind: "SqlGrant",
                role: "VIEW DEFINITION",
                azCommand: null,
                grantStatement: "GRANT VIEW DEFINITION TO [dev@contoso.com];",
            }),
        );
        expect(text).toContain("GRANT VIEW DEFINITION TO [dev@contoso.com];");
    });
});
