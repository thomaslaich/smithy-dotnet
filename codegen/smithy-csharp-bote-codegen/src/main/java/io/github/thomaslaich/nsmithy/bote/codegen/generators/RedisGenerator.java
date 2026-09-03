package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import io.github.thomaslaich.nsmithy.bote.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.bote.codegen.support.RedisBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.SchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.StructureShape;

/**
 * Generates StackExchange.Redis clients and owner-side dispatchers for Bote Redis JSON services.
 */
public final class RedisGenerator implements Runnable {
  public enum Kind {
    STREAMS,
    PUB_SUB
  }

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final Kind kind;

  public RedisGenerator(
      GenerationContext context, CSharpWriter writer, ServiceShape service, Kind kind) {
    this.context = context;
    this.writer = writer;
    this.service = service;
    this.kind = kind;
  }

  @Override
  public void run() {
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    writer.addImport(RuntimeTypes.NSMITHY_CODECS_JSON);
    writer.addImport(RuntimeTypes.STACKEXCHANGE_REDIS);
    writer.addImport("System.Runtime.CompilerServices");
    writer.addImport("System.Threading.Channels");

    String serviceName = CSharpNaming.typeName(service.getId().getName());
    if (kind == Kind.STREAMS) {
      writeStreams(serviceName);
    } else {
      writePubSub(serviceName);
    }
  }

  private void writeStreams(String serviceName) {
    Model model = context.model();
    List<RedisBindings.StreamAdd> adds =
        RedisBindings.streamAdds(model, context.symbolProvider(), service);
    List<RedisBindings.Subscription> reads =
        RedisBindings.streamReads(model, context.symbolProvider(), service);
    if (adds.isEmpty() && reads.isEmpty()) return;
    validateAddresses(
        adds.stream().map(RedisBindings.StreamAdd::stream).toList(),
        reads.stream().map(RedisBindings.Subscription::address).toList(),
        "Redis stream");

    writeStreamsClient(serviceName, adds, reads);
    if (!adds.isEmpty()) {
      writer.write("");
      writeStreamHandler(serviceName, adds);
      writer.write("");
      writeStreamConsumer(serviceName, adds);
    }
    writer.write("");
    writeReplyEnvelopeHelper();
  }

  private void writeStreamsClient(
      String serviceName,
      List<RedisBindings.StreamAdd> adds,
      List<RedisBindings.Subscription> reads) {
    String typeName = serviceName + "RedisStreams";
    writer.write("public sealed class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          Set<Shape> codecs = new LinkedHashSet<>();
          for (RedisBindings.StreamAdd add : adds) {
            codecs.add(add.command());
            add.reply().ifPresent(codecs::add);
          }
          for (RedisBindings.Subscription read : reads) codecs.add(read.union());
          codecs.forEach(this::writeCodecField);
          writer.write("private readonly IDatabase _database;");
          writer.write("private readonly ISubscriber _subscriber;");
          writer.write("");
          writer.write("public $L(IConnectionMultiplexer connection, int database = -1)", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(connection);");
                writer.write("_database = connection.GetDatabase(database);");
                writer.write("_subscriber = connection.GetSubscriber();");
              });

          for (RedisBindings.StreamAdd add : adds) {
            writer.write("");
            if (add.reply().isPresent()) writeRequestReplyMethod(add);
            else writeStreamAddMethod(add);
          }
          for (RedisBindings.Subscription read : reads) {
            for (MemberShape member : read.members()) {
              writer.write("");
              writeStreamEventPublishMethod(read, member);
            }
            writer.write("");
            writeStreamReadMethod(read);
          }
        });
  }

  private void writeStreamAddMethod(RedisBindings.StreamAdd add) {
    writer.write(
        "public async System.Threading.Tasks.Task $LAsync($L command,"
            + " System.Threading.CancellationToken cancellationToken = default)",
        add.opName(),
        add.commandType());
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(command);");
          writer.write("var payload = $L.Serialize(command);", codecFieldName(add.commandType()));
          writeStreamAddCall(
              add.stream(), add.maxLen().orElse(null), "new NameValueEntry(\"data\", payload)");
        });
  }

  private void writeRequestReplyMethod(RedisBindings.StreamAdd add) {
    String replyType = add.replyType().orElseThrow();
    writer.write(
        "public async System.Threading.Tasks.Task<$L> $LAsync($L command, System.TimeSpan? timeout"
            + " = null, System.Threading.CancellationToken cancellationToken = default)",
        replyType,
        add.opName(),
        add.commandType());
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(command);");
          writer.write("var correlationId = System.Guid.NewGuid().ToString(\"N\");");
          writer.write("var replyTo = \"bote:reply:\" + correlationId;");
          writer.write("var channel = RedisChannel.Literal(replyTo);");
          writer.write(
              "var completion = new"
                  + " System.Threading.Tasks.TaskCompletionSource<byte[]>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
          writer.write("System.Action<RedisChannel, RedisValue> onReply = (_, value) =>");
          writer.openBlock(
              "{",
              "};",
              () -> {
                writer.write("try");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("var reply = BoteRedisReplyEnvelope.Parse(value);");
                      writer.write("if (reply.CorrelationId == correlationId)");
                      writer.openBlock(
                          "{", "}", () -> writer.write("completion.TrySetResult(reply.Payload);"));
                    });
                writer.write("catch (System.Exception exception)");
                writer.openBlock(
                    "{", "}", () -> writer.write("completion.TrySetException(exception);"));
              });
          writer.write(
              "await _subscriber.SubscribeAsync(channel, onReply).WaitAsync(cancellationToken);");
          writer.write("try");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "var payload = $L.Serialize(command);", codecFieldName(add.commandType()));
                writeStreamAddCall(
                    add.stream(),
                    add.maxLen().orElse(null),
                    "new NameValueEntry(\"data\", payload), new NameValueEntry(\"reply_to\","
                        + " replyTo), new NameValueEntry(\"correlation_id\", correlationId)");
                writer.write(
                    "using var timeoutSource ="
                        + " System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);");
                writer.write(
                    "timeoutSource.CancelAfter(timeout ?? System.TimeSpan.FromSeconds(30));");
                writer.write(
                    "var replyPayload = await completion.Task.WaitAsync(timeoutSource.Token);");
                writer.write("return $L.Deserialize(replyPayload);", codecFieldName(replyType));
              });
          writer.write("finally");
          writer.openBlock(
              "{",
              "}",
              () -> writer.write("await _subscriber.UnsubscribeAsync(channel, onReply);"));
        });
  }

  private void writeStreamEventPublishMethod(RedisBindings.Subscription read, MemberShape member) {
    String eventType = qualified(member);
    String variant = CSharpNaming.typeName(member.getMemberName());
    writer.write(
        "public async System.Threading.Tasks.Task Publish$LAsync($L message,"
            + " System.Threading.CancellationToken cancellationToken = default)",
        variant,
        eventType);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(message);");
          writer.write("var wrapped = $L.From$L(message);", read.unionType(), variant);
          writer.write("var payload = $L.Serialize(wrapped);", codecFieldName(read.unionType()));
          writeStreamAddCall(
              read.address(), read.maxLen().orElse(null), "new NameValueEntry(\"data\", payload)");
        });
  }

  private void writeStreamReadMethod(RedisBindings.Subscription read) {
    writer.write(
        "public async System.Collections.Generic.IAsyncEnumerable<$L> $LAsync(RedisValue position ="
            + " default, int count = 10, [EnumeratorCancellation]"
            + " System.Threading.CancellationToken cancellationToken = default)",
        read.unionType(),
        read.opName());
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("if (position.IsNull) position = \"0-0\";");
          writer.write("else if (position == \"$$\")");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "var latest = await _database.StreamRangeAsync($L, \"-\", \"+\", 1,"
                        + " Order.Descending).WaitAsync(cancellationToken);",
                    literal(read.address()));
                writer.write("position = latest.Length == 0 ? \"0-0\" : latest[0].Id;");
              });
          writer.write("while (!cancellationToken.IsCancellationRequested)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "var entries = await _database.StreamReadAsync($L, position,"
                        + " count).WaitAsync(cancellationToken);",
                    literal(read.address()));
                writer.write("if (entries.Length == 0)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write(
                          "await System.Threading.Tasks.Task.Delay(100, cancellationToken);");
                      writer.write("continue;");
                    });
                writer.write("foreach (var entry in entries)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("position = entry.Id;");
                      writer.write(
                          "var payload = (byte[]?)BoteRedisEntry.GetRequired(entry, \"data\") ??"
                              + " throw new System.InvalidOperationException(\"Redis stream data is"
                              + " null.\");");
                      writer.write(
                          "yield return $L.Deserialize(payload);",
                          codecFieldName(read.unionType()));
                    });
              });
        });
  }

  private void writeStreamHandler(String serviceName, List<RedisBindings.StreamAdd> adds) {
    writer.write("public interface I$LRedisStreamsHandler", serviceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean first = true;
          for (RedisBindings.StreamAdd add : adds) {
            if (!first) writer.write("");
            first = false;
            if (add.replyType().isPresent()) {
              writer.write(
                  "System.Threading.Tasks.Task<$L> Handle$LAsync($L command,"
                      + " System.Threading.CancellationToken cancellationToken = default);",
                  add.replyType().orElseThrow(),
                  add.opName(),
                  add.commandType());
            } else {
              writer.write(
                  "System.Threading.Tasks.Task Handle$LAsync($L command,"
                      + " System.Threading.CancellationToken cancellationToken = default);",
                  add.opName(),
                  add.commandType());
            }
          }
        });
  }

  private void writeStreamConsumer(String serviceName, List<RedisBindings.StreamAdd> adds) {
    String typeName = serviceName + "RedisStreamsConsumer";
    String handlerType = "I" + serviceName + "RedisStreamsHandler";
    writer.write("public sealed class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          Set<Shape> codecs = new LinkedHashSet<>();
          for (RedisBindings.StreamAdd add : adds) {
            codecs.add(add.command());
            add.reply().ifPresent(codecs::add);
          }
          codecs.forEach(this::writeCodecField);
          writer.write("private readonly IDatabase _database;");
          writer.write("private readonly ISubscriber _subscriber;");
          writer.write("private readonly $L _handler;", handlerType);
          writer.write("private readonly string _group;");
          writer.write("private readonly string _consumer;");
          writer.write("");
          writer.write(
              "public $L(IConnectionMultiplexer connection, $L handler, string consumerGroup,"
                  + " string consumerName, int database = -1)",
              typeName,
              handlerType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(connection);");
                writer.write("_database = connection.GetDatabase(database);");
                writer.write("_subscriber = connection.GetSubscriber();");
                writer.write(
                    "_handler = handler ?? throw new"
                        + " System.ArgumentNullException(nameof(handler));");
                writer.write(
                    "_group = consumerGroup ?? throw new"
                        + " System.ArgumentNullException(nameof(consumerGroup));");
                writer.write(
                    "_consumer = consumerName ?? throw new"
                        + " System.ArgumentNullException(nameof(consumerName));");
              });
          writer.write("");
          writeEnsureGroups(adds);
          writer.write("");
          writer.write(
              "public async System.Threading.Tasks.Task RunAsync(int count = 10,"
                  + " System.Threading.CancellationToken cancellationToken = default)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("await EnsureConsumerGroupsAsync(cancellationToken);");
                writer.write("while (!cancellationToken.IsCancellationRequested)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("var received = false;");
                      for (RedisBindings.StreamAdd add : adds) writeStreamConsume(add);
                      writer.write(
                          "if (!received) await System.Threading.Tasks.Task.Delay(100,"
                              + " cancellationToken);");
                    });
              });
        });
    writer.write("");
    writeEntryHelper();
  }

  private void writeEnsureGroups(List<RedisBindings.StreamAdd> adds) {
    writer.write(
        "private async System.Threading.Tasks.Task"
            + " EnsureConsumerGroupsAsync(System.Threading.CancellationToken cancellationToken)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (String stream :
              adds.stream().map(RedisBindings.StreamAdd::stream).distinct().toList()) {
            writer.write("try");
            writer.openBlock(
                "{",
                "}",
                () ->
                    writer.write(
                        "await _database.StreamCreateConsumerGroupAsync($L, _group, \"0-0\","
                            + " createStream: true).WaitAsync(cancellationToken);",
                        literal(stream)));
            writer.write(
                "catch (RedisServerException exception) when"
                    + " (exception.Message.StartsWith(\"BUSYGROUP\","
                    + " System.StringComparison.Ordinal))");
            writer.openBlock("{", "}", () -> {});
          }
        });
  }

  private void writeStreamConsume(RedisBindings.StreamAdd add) {
    writer.write(
        "var $LEntries = await _database.StreamReadGroupAsync($L, _group, _consumer, \">\", count:"
            + " count).WaitAsync(cancellationToken);",
        CSharpNaming.parameterName(add.opName()),
        literal(add.stream()));
    writer.write("foreach (var entry in $LEntries)", CSharpNaming.parameterName(add.opName()));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("received = true;");
          writer.write(
              "var payload = (byte[]?)BoteRedisEntry.GetRequired(entry, \"data\") ?? throw new"
                  + " System.InvalidOperationException(\"Redis stream data is null.\");");
          writer.write("var command = $L.Deserialize(payload);", codecFieldName(add.commandType()));
          if (add.replyType().isPresent()) {
            writer.write(
                "var reply = await _handler.Handle$LAsync(command, cancellationToken);",
                add.opName());
            writer.write("var replyTo = (string)BoteRedisEntry.GetRequired(entry, \"reply_to\")!;");
            writer.write(
                "var correlationId = (string)BoteRedisEntry.GetRequired(entry,"
                    + " \"correlation_id\")!;");
            writer.write(
                "var replyPayload = $L.Serialize(reply);",
                codecFieldName(add.replyType().orElseThrow()));
            writer.write(
                "await _subscriber.PublishAsync(RedisChannel.Literal(replyTo),"
                    + " BoteRedisReplyEnvelope.Create(correlationId,"
                    + " replyPayload)).WaitAsync(cancellationToken);");
          } else {
            writer.write("await _handler.Handle$LAsync(command, cancellationToken);", add.opName());
          }
          writer.write(
              "await _database.StreamAcknowledgeAsync($L, _group,"
                  + " entry.Id).WaitAsync(cancellationToken);",
              literal(add.stream()));
        });
  }

  private void writePubSub(String serviceName) {
    Model model = context.model();
    List<RedisBindings.Publish> publishes =
        RedisBindings.publishes(model, context.symbolProvider(), service);
    List<RedisBindings.Subscription> subscribes =
        RedisBindings.subscribes(model, context.symbolProvider(), service);
    if (publishes.isEmpty() && subscribes.isEmpty()) return;
    validateAddresses(
        publishes.stream().map(RedisBindings.Publish::channel).toList(),
        subscribes.stream().map(RedisBindings.Subscription::address).toList(),
        "Redis Pub/Sub channel");

    String typeName = serviceName + "RedisPubSub";
    writer.write("public sealed class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          Set<Shape> codecs = new LinkedHashSet<>();
          for (RedisBindings.Publish publish : publishes) codecs.add(publish.command());
          for (RedisBindings.Subscription subscribe : subscribes) codecs.add(subscribe.union());
          codecs.forEach(this::writeCodecField);
          writer.write("private readonly ISubscriber _subscriber;");
          writer.write("");
          writer.write("public $L(IConnectionMultiplexer connection)", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(connection);");
                writer.write("_subscriber = connection.GetSubscriber();");
              });
          for (RedisBindings.Publish publish : publishes) {
            writer.write("");
            writer.write(
                "public async System.Threading.Tasks.Task $LAsync($L command,"
                    + " System.Threading.CancellationToken cancellationToken = default)",
                publish.opName(),
                publish.commandType());
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write("System.ArgumentNullException.ThrowIfNull(command);");
                  writer.write(
                      "var payload = $L.Serialize(command);",
                      codecFieldName(publish.commandType()));
                  writer.write(
                      "await _subscriber.PublishAsync(RedisChannel.Literal($L),"
                          + " payload).WaitAsync(cancellationToken);",
                      literal(publish.channel()));
                });
          }
          for (RedisBindings.Subscription subscribe : subscribes) {
            for (MemberShape member : subscribe.members()) {
              writer.write("");
              String variant = CSharpNaming.typeName(member.getMemberName());
              writer.write(
                  "public async System.Threading.Tasks.Task Publish$LAsync($L message,"
                      + " System.Threading.CancellationToken cancellationToken = default)",
                  variant,
                  qualified(member));
              writer.openBlock(
                  "{",
                  "}",
                  () -> {
                    writer.write("System.ArgumentNullException.ThrowIfNull(message);");
                    writer.write(
                        "var wrapped = $L.From$L(message);", subscribe.unionType(), variant);
                    writer.write(
                        "var payload = $L.Serialize(wrapped);",
                        codecFieldName(subscribe.unionType()));
                    writer.write(
                        "await _subscriber.PublishAsync(RedisChannel.Literal($L),"
                            + " payload).WaitAsync(cancellationToken);",
                        literal(subscribe.address()));
                  });
            }
            writer.write("");
            writePubSubSubscribeMethod(subscribe);
          }
        });
    if (!publishes.isEmpty()) {
      writer.write("");
      writePubSubHandler(serviceName, publishes);
      writer.write("");
      writePubSubConsumer(serviceName, publishes);
    }
  }

  private void writePubSubHandler(String serviceName, List<RedisBindings.Publish> publishes) {
    writer.write("public interface I$LRedisPubSubHandler", serviceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean first = true;
          for (RedisBindings.Publish publish : publishes) {
            if (!first) writer.write("");
            first = false;
            writer.write(
                "System.Threading.Tasks.Task Handle$LAsync($L command,"
                    + " System.Threading.CancellationToken cancellationToken = default);",
                publish.opName(),
                publish.commandType());
          }
        });
  }

  private void writePubSubConsumer(String serviceName, List<RedisBindings.Publish> publishes) {
    String typeName = serviceName + "RedisPubSubConsumer";
    String handlerType = "I" + serviceName + "RedisPubSubHandler";
    writer.write("public sealed class $L", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (RedisBindings.Publish publish : publishes) writeCodecField(publish.command());
          writer.write("private readonly ISubscriber _subscriber;");
          writer.write("private readonly $L _handler;", handlerType);
          writer.write("");
          writer.write(
              "public $L(IConnectionMultiplexer connection, $L handler)", typeName, handlerType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(connection);");
                writer.write("_subscriber = connection.GetSubscriber();");
                writer.write(
                    "_handler = handler ?? throw new"
                        + " System.ArgumentNullException(nameof(handler));");
              });
          writer.write("");
          writer.write(
              "public async System.Threading.Tasks.Task RunAsync(System.Threading.CancellationToken"
                  + " cancellationToken = default)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "var messages = Channel.CreateUnbounded<(RedisChannel Address, RedisValue"
                        + " Value)>();");
                writer.write(
                    "System.Action<RedisChannel, RedisValue> onMessage = (address, value) =>"
                        + " messages.Writer.TryWrite((address, value));");
                for (RedisBindings.Publish publish : publishes) {
                  writer.write(
                      "await _subscriber.SubscribeAsync(RedisChannel.Literal($L),"
                          + " onMessage).WaitAsync(cancellationToken);",
                      literal(publish.channel()));
                }
                writer.write("try");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write(
                          "await foreach (var message in"
                              + " messages.Reader.ReadAllAsync(cancellationToken))");
                      writer.openBlock(
                          "{",
                          "}",
                          () -> {
                            for (int i = 0; i < publishes.size(); i++) {
                              RedisBindings.Publish publish = publishes.get(i);
                              writer.write(
                                  "$L (message.Address == RedisChannel.Literal($L))",
                                  i == 0 ? "if" : "else if",
                                  literal(publish.channel()));
                              writer.openBlock(
                                  "{",
                                  "}",
                                  () -> {
                                    writer.write(
                                        "var payload = (byte[]?)message.Value ?? throw new"
                                            + " System.InvalidOperationException(\"Redis Pub/Sub"
                                            + " payload is null.\");");
                                    writer.write(
                                        "var command = $L.Deserialize(payload);",
                                        codecFieldName(publish.commandType()));
                                    writer.write(
                                        "await _handler.Handle$LAsync(command, cancellationToken);",
                                        publish.opName());
                                  });
                            }
                          });
                    });
                writer.write("finally");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      for (RedisBindings.Publish publish : publishes) {
                        writer.write(
                            "await _subscriber.UnsubscribeAsync(RedisChannel.Literal($L),"
                                + " onMessage);",
                            literal(publish.channel()));
                      }
                    });
              });
        });
  }

  private void writePubSubSubscribeMethod(RedisBindings.Subscription subscribe) {
    writer.write(
        "public async System.Collections.Generic.IAsyncEnumerable<$L>"
            + " $LAsync([EnumeratorCancellation] System.Threading.CancellationToken"
            + " cancellationToken = default)",
        subscribe.unionType(),
        subscribe.opName());
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("var messages = Channel.CreateUnbounded<RedisValue>();");
          writer.write("var channel = RedisChannel.Literal($L);", literal(subscribe.address()));
          writer.write(
              "System.Action<RedisChannel, RedisValue> onMessage = (_, value) =>"
                  + " messages.Writer.TryWrite(value);");
          writer.write(
              "await _subscriber.SubscribeAsync(channel, onMessage).WaitAsync(cancellationToken);");
          writer.write("try");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "await foreach (var value in messages.Reader.ReadAllAsync(cancellationToken))");
                writer.openBlock(
                    "{",
                    "}",
                    () ->
                        writer.write(
                            "yield return $L.Deserialize((byte[]?)value ??"
                                + " System.Array.Empty<byte>());",
                            codecFieldName(subscribe.unionType())));
              });
          writer.write("finally");
          writer.openBlock(
              "{",
              "}",
              () -> writer.write("await _subscriber.UnsubscribeAsync(channel, onMessage);"));
        });
  }

  private void writeStreamAddCall(String stream, Long maxLen, String entries) {
    String max = maxLen == null ? "null" : Integer.toString(Math.toIntExact(maxLen));
    writer.write(
        "await _database.StreamAddAsync($L, new NameValueEntry[] { $L }, maxLength: $L,"
            + " useApproximateMaxLength: $L).WaitAsync(cancellationToken);",
        literal(stream),
        entries,
        max,
        maxLen == null ? "false" : "true");
  }

  private void writeCodecField(Shape shape) {
    String type = context.symbolProvider().toSymbol(shape).getFullName();
    writer.write(
        "private static readonly ICodec<$L> $L = JsonCodecFactory.Default.FromSchema($L.Schema);",
        type,
        codecFieldName(type),
        SchemaGenerator.schemaClassName(context, shape));
  }

  private void writeEntryHelper() {
    writer.write("internal static class BoteRedisEntry");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static RedisValue GetRequired(StreamEntry entry, RedisValue name)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("foreach (var field in entry.Values)");
                writer.openBlock(
                    "{", "}", () -> writer.write("if (field.Name == name) return field.Value;"));
                writer.write(
                    "throw new System.InvalidOperationException(\"Redis stream entry is missing"
                        + " field '\" + name + \"'.\");");
              });
        });
  }

  private void writeReplyEnvelopeHelper() {
    writer.write("internal static class BoteRedisReplyEnvelope");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "internal readonly record struct Reply(string CorrelationId, byte[] Payload);");
          writer.write("");
          writer.write("public static byte[] Create(string correlationId, byte[] payload)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("using var stream = new System.IO.MemoryStream();");
                writer.write("using (var json = new System.Text.Json.Utf8JsonWriter(stream))");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      writer.write("json.WriteStartObject();");
                      writer.write("json.WriteString(\"correlation_id\", correlationId);");
                      writer.write("json.WritePropertyName(\"data\");");
                      writer.write("json.WriteRawValue(payload, skipInputValidation: false);");
                      writer.write("json.WriteEndObject();");
                    });
                writer.write("return stream.ToArray();");
              });
          writer.write("");
          writer.write("public static Reply Parse(RedisValue value)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("var bytes = (byte[]?)value ?? System.Array.Empty<byte>();");
                writer.write("using var json = System.Text.Json.JsonDocument.Parse(bytes);");
                writer.write("var root = json.RootElement;");
                writer.write(
                    "var correlationId = root.GetProperty(\"correlation_id\").GetString() ?? throw"
                        + " new System.Text.Json.JsonException(\"Missing correlation_id.\");");
                writer.write(
                    "var payload ="
                        + " System.Text.Encoding.UTF8.GetBytes(root.GetProperty(\"data\").GetRawText());");
                writer.write("return new Reply(correlationId, payload);");
              });
        });
  }

  private String qualified(MemberShape member) {
    StructureShape shape = context.model().expectShape(member.getTarget(), StructureShape.class);
    return context.symbolProvider().toSymbol(shape).getFullName();
  }

  private static String codecFieldName(String qualifiedType) {
    return qualifiedType.replace('.', '_').replace('?', '_') + "Codec";
  }

  private static String literal(String value) {
    return CSharpNaming.formatString(value);
  }

  private static void validateAddresses(
      List<String> inbound, List<String> outbound, String addressKind) {
    Set<String> inboundUnique = new LinkedHashSet<>(inbound);
    Set<String> outboundUnique = new LinkedHashSet<>(outbound);
    if (inboundUnique.size() != inbound.size()) {
      throw new CodegenException(addressKind + " has more than one command operation.");
    }
    if (outboundUnique.size() != outbound.size()) {
      throw new CodegenException(addressKind + " has more than one event operation.");
    }
    inboundUnique.retainAll(outboundUnique);
    if (!inboundUnique.isEmpty()) {
      throw new CodegenException(
          addressKind
              + " cannot carry both commands and events without a direction discriminator: "
              + inboundUnique);
    }
  }
}
