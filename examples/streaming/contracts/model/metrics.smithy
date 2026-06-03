$version: "2"

namespace example.metrics

use alloy.proto#grpc
use alloy.proto#protoIndex
use alloy.proto#protoNumType

// ─── Service ──────────────────────────────────────────────────────────────────

/// A metrics collection service that demonstrates all three gRPC streaming modes:
///
///   - StreamMetrics : server streaming  (server pushes metric updates to client)
///   - RecordMetrics : client streaming  (client pushes metric readings to server)
///   - MonitorMetrics: bidirectional     (client updates filter; server streams matches)
@grpc
service MetricsService {
    version: "2026-05-30"
    operations: [StreamMetrics, RecordMetrics, MonitorMetrics]
}

// ─── Server streaming ─────────────────────────────────────────────────────────
//
//   proto:  rpc StreamMetrics (StreamMetricsInput) returns (stream StreamMetricsOutputEvent);
//   C# handler: IAsyncEnumerable<StreamMetricsOutputEvent> StreamMetricsAsync(
//                   StreamMetricsInput input, CancellationToken ct);

/// Subscribe to live metric updates. The server streams one StreamMetricsOutputEvent
/// per metric reading until the client cancels or the server exhausts the series.
operation StreamMetrics {
    input: StreamMetricsInput
    output: StreamMetricsOutput
}

structure StreamMetricsInput {
    /// Only stream metrics whose name starts with this prefix (empty = all).
    @protoIndex(1)
    prefix: String

    /// Stop after this many readings (0 = unlimited).
    @protoIndex(2)
    @protoNumType("UNSIGNED")
    maxSamples: Integer
}

structure StreamMetricsOutput {
    @protoIndex(1)
    events: StreamMetricsOutputEvent
}

@streaming
union StreamMetricsOutputEvent {
    @protoIndex(1)
    reading: MetricReading
}

structure MetricReading {
    /// Metric name (e.g. "cpu.usage", "memory.free_mb").
    @required
    @protoIndex(1)
    name: String

    /// Numeric value.
    @required
    @protoIndex(2)
    value: Double

    /// Unit string (e.g. "percent", "mb", "req/s").
    @required
    @protoIndex(3)
    unit: String
}

// ─── Client streaming ─────────────────────────────────────────────────────────
//
//   proto:  rpc RecordMetrics (stream RecordMetricsInputEvent) returns (RecordMetricsOutput);
//   C# handler: Task<RecordMetricsOutput> RecordMetricsAsync(
//                   IAsyncEnumerable<RecordMetricsInputEvent> input, CancellationToken ct);

/// Upload a stream of metric readings. Returns a summary once the client
/// signals completion (closes the request stream).
operation RecordMetrics {
    input: RecordMetricsInput
    output: RecordMetricsOutput
}

structure RecordMetricsInput {
    @protoIndex(1)
    events: RecordMetricsInputEvent
}

@streaming
union RecordMetricsInputEvent {
    @protoIndex(1)
    reading: MetricReading
}

structure RecordMetricsOutput {
    /// Total number of metric readings accepted by the server.
    @required
    @protoIndex(1)
    @protoNumType("UNSIGNED")
    recordedCount: Integer
}

// ─── Bidirectional streaming ──────────────────────────────────────────────────
//
//   proto:  rpc MonitorMetrics (stream MonitorMetricsInputEvent) returns (stream MonitorMetricsOutputEvent);
//   C# handler: IAsyncEnumerable<MonitorMetricsOutputEvent> MonitorMetricsAsync(
//                   IAsyncEnumerable<MonitorMetricsInputEvent> input, CancellationToken ct);

/// Live monitoring with dynamic filtering. The client streams filter-update
/// messages; the server streams back every metric reading that matches the
/// current filter. Sending a new filter replaces the previous one.
operation MonitorMetrics {
    input: MonitorMetricsInput
    output: MonitorMetricsOutput
}

structure MonitorMetricsInput {
    @protoIndex(1)
    events: MonitorMetricsInputEvent
}

@streaming
union MonitorMetricsInputEvent {
    @protoIndex(1)
    filter: MonitorMetricsFilter
}

structure MonitorMetricsFilter {
    /// Name prefix filter to apply from this point onwards (empty = all).
    @required
    @protoIndex(1)
    prefix: String
}

structure MonitorMetricsOutput {
    @protoIndex(1)
    events: MonitorMetricsOutputEvent
}

@streaming
union MonitorMetricsOutputEvent {
    @protoIndex(1)
    reading: MetricReading
}
