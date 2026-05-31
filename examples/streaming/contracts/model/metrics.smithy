$version: "2"

namespace example.metrics

use alloy.proto#grpc
use alloy.proto#protoIndex
use alloy.proto#protoNumType
use nsmithy.proto#grpcServerStream
use nsmithy.proto#grpcClientStream

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
//   proto:  rpc StreamMetrics (StreamMetricsInput) returns (stream StreamMetricsOutput);
//   C# handler: IAsyncEnumerable<StreamMetricsOutput> StreamMetricsAsync(
//                   StreamMetricsInput input, CancellationToken ct);

/// Subscribe to live metric updates. The server streams one StreamMetricsOutput
/// per metric reading until the client cancels or the server exhausts the series.
@grpcServerStream
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
//   proto:  rpc RecordMetrics (stream RecordMetricsInput) returns (RecordMetricsOutput);
//   C# handler: Task<RecordMetricsOutput> RecordMetricsAsync(
//                   IAsyncEnumerable<RecordMetricsInput> input, CancellationToken ct);

/// Upload a stream of metric readings. Returns a summary once the client
/// signals completion (closes the request stream).
@grpcClientStream
operation RecordMetrics {
    input: RecordMetricsInput
    output: RecordMetricsOutput
}

structure RecordMetricsInput {
    /// Metric name.
    @required
    @protoIndex(1)
    name: String

    /// Numeric value.
    @required
    @protoIndex(2)
    value: Double

    /// Unit string.
    @required
    @protoIndex(3)
    unit: String
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
//   proto:  rpc MonitorMetrics (stream MonitorMetricsInput) returns (stream MonitorMetricsOutput);
//   C# handler: IAsyncEnumerable<MonitorMetricsOutput> MonitorMetricsAsync(
//                   IAsyncEnumerable<MonitorMetricsInput> input, CancellationToken ct);

/// Live monitoring with dynamic filtering. The client streams filter-update
/// messages; the server streams back every metric reading that matches the
/// current filter. Sending a new filter replaces the previous one.
@grpcServerStream
@grpcClientStream
operation MonitorMetrics {
    input: MonitorMetricsInput
    output: MonitorMetricsOutput
}

structure MonitorMetricsInput {
    /// Name prefix filter to apply from this point onwards (empty = all).
    @required
    @protoIndex(1)
    prefix: String
}

structure MonitorMetricsOutput {
    /// Metric name.
    @required
    @protoIndex(1)
    name: String

    /// Numeric value.
    @required
    @protoIndex(2)
    value: Double

    /// Unit string.
    @required
    @protoIndex(3)
    unit: String
}
