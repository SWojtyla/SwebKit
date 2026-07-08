namespace SwebKit.Core.Domain;

public enum SbAuthMode { DefaultAzureCredential, ConnectionString, ServicePrincipal }

/// <summary>
/// Data-plane transport used for the Service Bus SDK connection. Defaults to <see cref="Amqp"/>
/// (AMQP over TCP, port 5671) for backward compatibility with existing persisted configs.
/// Use <see cref="AmqpWebSockets"/> (port 443) when a network path blocks plain AMQP.
/// </summary>
public enum SbTransportType { Amqp, AmqpWebSockets }

public enum EntityType { Queue, Topic, Subscription, Deployment }

public enum PipelineRunState { Unknown, InProgress, Canceling, Completed }

public enum PipelineRunResult { Unknown, Succeeded, Failed, Canceled }
