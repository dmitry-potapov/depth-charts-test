# Ensure the command topic exists before subscribing

Problem: `AllowAutoCreateTopics` on the consumer did not create `depth-chart-commands`; the Api logged `Subscribed topic not available` until the producer first published.

- `KafkaCommandConsumer.RunAsync` creates the topic with an `IAdminClient` (1 partition, broker-default replication) before `Subscribe`; `TopicAlreadyExists` is ignored.
- Admin failures (e.g. no broker) are logged as warnings so the Api still starts without Kafka.
- `AllowAutoCreateTopics` removed from `ConsumerConfig`.
- Tests: `KafkaCommandConsumerTests.EnsureTopic_*` (fake `IAdminClient` via `DispatchProxy`).
