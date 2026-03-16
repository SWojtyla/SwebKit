# Microsoft Store Submission Guide for SwebKit

This document provides guidance on using the privacy policy files and completing the Microsoft Store submission for SwebKit.

## Files Included

1. **PRIVACY_POLICY.md** - Comprehensive privacy policy in Markdown format
   - Use for: Website, GitHub repository, detailed documentation
   - Full legal privacy policy with all details

2. **PRIVACY_POLICY_SHORT.md** - Concise version with Microsoft Store statement
   - Use for: Quick reference, app store descriptions
   - Contains a specific "Privacy Statement for Microsoft Store" section at the bottom

3. **PRIVACY_POLICY.txt** - Plain text version
   - Use for: Copying directly into forms or text editors
   - Same content as the full policy but in plain text format

## How to Use for Microsoft Store Submission

### Step 1: Privacy Policy URL

Microsoft Store requires a privacy policy URL. You have two options:

**Option A: Host on GitHub Pages**

1. Enable GitHub Pages in your repository settings
2. Use the URL: `https://swojtyla.github.io/SwebKit/PRIVACY_POLICY.html`
3. You may need to create an HTML version or let GitHub Pages convert the Markdown

**Option B: Use GitHub Raw URL**
Use: `https://raw.githubusercontent.com/SWojtyla/SwebKit/main/PRIVACY_POLICY.md`

**Option C: Host on Your Own Website**
Copy the content from `PRIVACY_POLICY.md` to your website

### Step 2: Microsoft Store Privacy Statement

When submitting to Microsoft Store, you'll be asked to provide a privacy statement. Use the text from the bottom of `PRIVACY_POLICY_SHORT.md`:

```
SwebKit does not collect, use, or share any user data. All data (Azure connection strings, Kubernetes credentials, and project configurations) is stored locally on the user's device in encrypted form. The application connects directly to the user's own Azure and Kubernetes resources and does not transmit any data to third parties or our servers. No analytics, tracking, or advertising services are used.
```

### Step 3: App Permissions Declaration

When declaring app permissions in Microsoft Store, mention:

**Desktop App Permissions:**

- **File System Access**: Required to save encrypted connection strings, credentials, and project configurations locally; required to read kubeconfig for Kubernetes access
- **Network Access**: Required to connect to Azure Service Bus namespaces, Azure Monitor / Application Insights workspaces, and AKS / Kubernetes clusters

### Step 4: Data Collection Declaration

In Microsoft Store submission, when asked about data collection:

- **Personal Information**: No
- **User Content**: No (messages and logs are temporarily displayed, not collected)
- **Browsing History**: No
- **Usage Data**: No
- **Diagnostics Data**: No
- **Location**: No
- **Contacts**: No
- **Financial Info**: No

### Step 5: Data Sharing Declaration

- **Do you share data with third parties?**: No
- **Do you use analytics services?**: No
- **Do you use advertising networks?**: No

## Microsoft Store Categories

For app categorization:

- **Primary Category**: Developer Tools
- **Sub-category**: Development Tools / Cloud Tools
- **Age Rating**: Everyone (no age restrictions needed)

## App Description (Short)

> SwebKit is a developer toolkit for .NET developers working with Azure. Inspect Azure Service Bus queues and topics, query Application Insights logs and traces, and debug AKS workloads — all in one desktop app scoped to your projects and environments.

## App Description (Long)

> SwebKit is a .NET MAUI Blazor Hybrid desktop "Swiss army knife" for .NET developers working with Azure cloud infrastructure.
>
> **Azure Service Bus**
> - Inspect messages in queues, topics, and subscriptions
> - Fix dead-letter queue (DLQ) messages
> - Send and replay test messages
>
> **Observability (Application Insights / OpenTelemetry)**
> - Query Application Insights logs
> - Explore distributed traces
> - View metrics dashboards
>
> **AKS / Kubernetes**
> - Workload overview and pod management
> - Live pod log tailing
> - Port-forwarding and pod shell access
> - StatefulSets, ConfigMaps, Secrets, and HPA visibility
>
> **Project & Environment scoping**
> - Organize everything by project (e.g. "OrderPlatform") and environment (Dev / Test / Acc / Prod)
>
> All credentials are stored locally and encrypted. No data is sent to third-party servers.

## Additional Recommendations

1. **Keep Privacy Policy Updated**: Update the "Last Updated" date whenever you make changes
2. **Version in App**: Consider adding a "Privacy Policy" link in the app that opens the policy
3. **Changelog**: If you add new features that affect privacy, update the policy
4. **GitHub Repository**: Link the policy in README.md

## Common Microsoft Store Questions

**Q: Does your app collect user data?**
A: No. All data is stored locally on the user's device.

**Q: Does your app transmit data to your servers?**
A: No. The app only connects directly to the user's own Azure Service Bus, Application Insights, and AKS resources.

**Q: Does your app use encryption?**
A: Yes. All connection strings and credentials are encrypted using Microsoft's Data Protection API.

**Q: Does your app require internet access?**
A: Yes, to connect to Azure and Kubernetes resources (the user's own cloud infrastructure).

**Q: What data does your app access?**
A: Only data from the user's own Azure Service Bus, Application Insights, and AKS resources that they explicitly connect to.

## Testing Before Submission

Before submitting to Microsoft Store:

1. Test the privacy policy URL is accessible
2. Verify all permissions are accurately declared
3. Review the app for any analytics or tracking code
4. Ensure credential encryption is working
5. Test local storage and file system functionality
6. Verify MSIX package builds and installs correctly

## Contact for Questions

If you have questions about this privacy policy or need modifications:

- Open an issue on GitHub: https://github.com/SWojtyla/SwebKit/issues
- Review Microsoft Store Requirements: https://learn.microsoft.com/en-us/windows/apps/publish/

## Legal Disclaimer

This privacy policy template is provided as-is. While it's designed to comply with common privacy regulations including GDPR and CCPA, you should review it with legal counsel if you have specific concerns or requirements. The developer is responsible for ensuring compliance with all applicable laws and regulations.
