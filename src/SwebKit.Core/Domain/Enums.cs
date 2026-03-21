namespace SwebKit.Core.Domain;

public enum SbAuthMode { DefaultAzureCredential, ConnectionString, ServicePrincipal }

public enum EntityType { Queue, Topic, Subscription, Deployment }

public enum PipelineRunState { Unknown, InProgress, Canceling, Completed }

public enum PipelineRunResult { Unknown, Succeeded, Failed, Canceled }
