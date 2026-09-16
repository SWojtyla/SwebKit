import { describe, expect, it } from "vitest";
import { filterGeneratedAnnotations } from "./yaml-noise";

const manifest = `apiVersion: apps/v1
kind: Deployment
metadata:
  name: orders
  annotations:
    kubectl.kubernetes.io/last-applied-configuration: |-
      {"kind":"Deployment",
       "metadata":{"name":"orders"}}
    deployment.kubernetes.io/revision: "4"
    meta.helm.sh/release-name: orders
    keep.example.com/owner: team-a
spec:
  template:
    metadata:
      annotations:
        kubectl.kubernetes.io/last-applied-configuration: keep-nested
        rollme: "1"
`;

describe("filterGeneratedAnnotations", () => {
  it("removes generated top-level annotations and their block values", () => {
    const result = filterGeneratedAnnotations(manifest);

    expect(result.hidden).toBe(3);
    expect(result.yaml).not.toContain("{\"kind\":\"Deployment\"");
    expect(result.yaml).not.toContain("deployment.kubernetes.io/revision");
    expect(result.yaml).toContain("keep.example.com/owner: team-a");
  });

  it("does not remove nested pod-template annotations", () => {
    const result = filterGeneratedAnnotations(manifest);

    expect(result.yaml).toContain("kubectl.kubernetes.io/last-applied-configuration: keep-nested");
    expect(result.yaml).toContain('rollme: "1"');
  });

  it("leaves manifests without top-level annotations unchanged", () => {
    const yaml = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: x\ndata:\n  value: y\n";
    expect(filterGeneratedAnnotations(yaml)).toEqual({ yaml, hidden: 0 });
  });
});
