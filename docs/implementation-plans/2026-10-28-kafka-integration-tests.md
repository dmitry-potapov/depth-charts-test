# Kafka end-to-end integration tests

Coverage review (coverlet): 91% lines, 92% branches. Only one gap matters: `KafkaCommandConsumer.RunAsync`/`EnsureTopicAsync(KafkaOptions, …)`, the real consumer, admin client and subscribe path, which no test ran (about 48% of that file). The other uncovered lines are trivial: the Producer `Program.cs` composition root, the defensive unknown-command arm, `Team.ToString` and the `StoredEvent` setter.

- Added `Testcontainers.Kafka` to the test project.
- `tests/DepthCharts.Tests/Integration/KafkaEndToEndTests.cs` (one `apache/kafka:4.1.0` container per class, a unique topic per test):
  - `PublishedCommands_AreConsumedInOrder_AndServedByApi`: Api host with the consumer on, publish through `KafkaCommandPublisher`, then poll `GET /depth-charts/nfl/tb` until the chart appears.
  - `Restart_ReplaysTopicFromEarliest`: a second host on the same topic rebuilds the chart from Kafka.
- `dotnet test` now needs Docker. README and `docs/architecture.md` updated.
