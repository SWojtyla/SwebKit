namespace SwebKit.Core.Domain;

public enum EnvironmentTier { NonProd, Production }

public enum ObservabilityProviderType { AppInsights, OtlpEndpoint }

public enum SbAuthMode { DefaultAzureCredential, ConnectionString, ServicePrincipal }

public enum EntityType { Queue, Topic, Subscription, Deployment }

public enum QueryArea { Logs, Traces, Metrics }

public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical }

public enum SpanKind { Client, Server, Producer, Consumer, Internal }

public enum SpanStatus { Ok, Error, Unset }

public enum PipelineRunState { Unknown, InProgress, Canceling, Completed }

public enum PipelineRunResult { Unknown, Succeeded, Failed, Canceled }
