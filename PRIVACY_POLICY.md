# Privacy Policy for SwebKit

**Last Updated:** March 16, 2026

## Introduction

SwebKit ("the App") is a .NET MAUI Blazor Hybrid desktop developer toolkit for .NET developers working with Azure. It provides Azure Service Bus tooling, Application Insights / OpenTelemetry observability, and AKS (Kubernetes) debugging helpers in a single desktop application. This Privacy Policy describes how the App handles your information.

## Developer Information

**Developer:** SWojtyla
**Contact:** [GitHub Repository](https://github.com/SWojtyla/SwebKit)

## Data Collection and Usage

### What Data Does the App Access?

The App accesses the following information solely for the purpose of connecting to and inspecting your own Azure and Kubernetes resources:

1. **Azure Service Bus Connection Strings**: Required to connect to your Azure Service Bus namespace
2. **Azure Monitor / Application Insights API Keys or Connection Strings**: Required to query your Application Insights workspace
3. **Kubernetes Configuration (kubeconfig)**: Required to connect to your AKS clusters
4. **Project and Environment Configuration**: Your defined projects, environments, and saved connections
5. **Application Settings**: Your preferences and UI state

### How is Data Stored?

#### Desktop Application (.NET MAUI)

- **Connection Strings and Credentials**: Stored encrypted in local files on your device using .NET Data Protection API
- **Project/Environment Configuration**: Stored in local application data on your device
- **Application Preferences**: Stored in local application data on your device

All data is stored locally on your device. No data is uploaded to any server controlled by the developer.

### Data Security

- **Encryption**: All sensitive credentials (connection strings, API keys) are encrypted using Microsoft's Data Protection API before being stored
- **Local Storage Only**: All data is stored locally on your device. No information is transmitted to our servers or any third-party services
- **Direct Connection**: The App connects directly to your Azure and Kubernetes resources. We do not act as an intermediary or proxy

## Third-Party Services

The App does NOT share any data with third parties. The only external connections made are directly to resources you control:

- Your Azure Service Bus namespace
- Your Azure Monitor / Application Insights workspace
- Your AKS / Kubernetes clusters

## Data Retention

- Data remains on your local file system until you manually delete it
- You can delete individual saved connections and projects through the App's interface
- You can delete all App data by clearing the application data folder

## Your Rights and Control

You have complete control over your data:

- **Access**: All data is stored locally and accessible to you at any time
- **Deletion**: You can delete saved connections and projects at any time through the App
- **Portability**: Your configuration data is stored in standard JSON format and can be accessed directly
- **Control**: You can clear all App data by deleting local application data files

## Children's Privacy

The App is not intended for use by children under the age of 13. We do not knowingly collect any information from children.

## Azure and Kubernetes Data

The App acts as a client to your own cloud resources:

- Messages, logs, traces, and metrics retrieved from your Azure resources are displayed in the App but not stored permanently by the App
- Any modifications to your resources (sending messages, etc.) are performed directly through the Azure SDK and Kubernetes client
- We have no access to your Azure or Kubernetes data

## Analytics and Tracking

The App does NOT:

- Collect usage analytics
- Track your behavior
- Use cookies or web tracking
- Share any information with advertising networks
- Transmit telemetry data to our servers

## File System Access

The desktop application requires access to your file system for:

- Storing encrypted connection strings and credentials
- Reading your kubeconfig file to connect to Kubernetes clusters
- Saving application configuration and preferences

All file operations are performed locally on your device.

## Network Access

The App requires network access to:

- Connect to your Azure Service Bus namespace
- Query your Azure Monitor / Application Insights workspace
- Connect to your AKS / Kubernetes clusters

All network connections are made directly to your own cloud resources.

## Changes to This Privacy Policy

We may update this Privacy Policy from time to time. Any changes will be reflected in the "Last Updated" date at the top of this document. We encourage you to review this Privacy Policy periodically.

## Open Source

SwebKit is open source software. You can review the source code and verify our privacy practices at:
https://github.com/SWojtyla/SwebKit

## Consent

By using the App, you consent to this Privacy Policy.

## Contact Us

If you have any questions about this Privacy Policy or the App's privacy practices, please contact us through:

- GitHub Issues: https://github.com/SWojtyla/SwebKit/issues

## Legal Compliance

This Privacy Policy is designed to comply with:

- General Data Protection Regulation (GDPR)
- California Consumer Privacy Act (CCPA)
- Microsoft Store Privacy Requirements

## Summary

**In Plain English:**

- The App only stores your Azure and Kubernetes credentials and configuration locally on your device
- All sensitive data (connection strings, API keys) is encrypted
- We don't collect, track, or share any of your data
- The App connects directly to your own cloud resources — we never see your data
- You have complete control and can delete everything at any time
