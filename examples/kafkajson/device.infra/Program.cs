using System.Globalization;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Examples.Kafka.Streetlights;

var bootstrapServers = args.FirstOrDefault() ?? "localhost:9092";
using var admin = new AdminClientBuilder(
    new AdminClientConfig
    {
        BootstrapServers = bootstrapServers,
        BrokerAddressFamily = BrokerAddressFamily.V4,
    }
)
    .SetErrorHandler(
        (_, error) =>
        {
            if (error.IsFatal)
                Console.Error.WriteLine(error.Reason);
        }
    )
    .Build();

await WaitForKafkaAsync(admin);

foreach (var topic in StreetlightDeviceKafkaInfrastructure.Topics)
{
    var metadata = admin.GetMetadata(topic.Name, TimeSpan.FromSeconds(10));
    var existing = metadata.Topics.SingleOrDefault(candidate =>
        candidate.Topic == topic.Name && candidate.Error.Code == ErrorCode.NoError
    );

    if (existing is null)
    {
        try
        {
            await admin.CreateTopicsAsync([
                new TopicSpecification
                {
                    Name = topic.Name,
                    NumPartitions = topic.Partitions ?? -1,
                    ReplicationFactor = topic.ReplicationFactor ?? -1,
                    Configs = new Dictionary<string, string>(topic.Configuration),
                },
            ]);
        }
        catch (CreateTopicsException exception)
            when (exception.Results.All(result => result.Error.Code == ErrorCode.TopicAlreadyExists)
            ) { }

        Console.WriteLine($"created {topic.Name}");
        continue;
    }

    if (topic.Partitions is { } partitions && existing.Partitions.Count < partitions)
    {
        await admin.CreatePartitionsAsync([
            new PartitionsSpecification { Topic = topic.Name, IncreaseTo = partitions },
        ]);
    }
    else if (topic.Partitions is { } requested && existing.Partitions.Count > requested)
    {
        Console.Error.WriteLine(
            $"warning: {topic.Name} has {existing.Partitions.Count} partitions; Kafka cannot reduce it to {requested}"
        );
    }

    var currentReplicationFactor = existing.Partitions.FirstOrDefault()?.Replicas.Length ?? 0;
    if (
        topic.ReplicationFactor is { } replicationFactor
        && currentReplicationFactor != replicationFactor
    )
    {
        Console.Error.WriteLine(
            $"warning: {topic.Name} has replication factor {currentReplicationFactor}; requested {replicationFactor} requires reassignment"
        );
    }

    if (topic.Configuration.Count > 0)
    {
        var resource = new ConfigResource { Type = ResourceType.Topic, Name = topic.Name };
        await admin.IncrementalAlterConfigsAsync(
            new Dictionary<ConfigResource, List<ConfigEntry>>
            {
                [resource] =
                [
                    .. topic.Configuration.Select(entry => new ConfigEntry
                    {
                        Name = entry.Key,
                        Value = entry.Value,
                        IncrementalOperation = AlterConfigOpType.Set,
                    }),
                ],
            }
        );
    }

    Console.WriteLine(
        $"deployed {topic.Name} ({topic.Partitions?.ToString(CultureInfo.InvariantCulture) ?? "broker default"} partition(s), replication factor {topic.ReplicationFactor?.ToString(CultureInfo.InvariantCulture) ?? "broker default"})"
    );
}

static async Task WaitForKafkaAsync(IAdminClient admin)
{
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try
        {
            admin.GetMetadata(TimeSpan.FromSeconds(2));
            return;
        }
        catch (KafkaException) when (attempt < 30)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    throw new TimeoutException("Kafka did not become ready in time.");
}
