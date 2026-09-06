/*
 * Renders an opt-in IHttpClientFactory registration extension for a service client.
 *
 * Emits a `{Service}ClientServiceCollectionExtensions` class with `Add{Service}Client(...)`
 * methods that register the generated `{Service}Client` as a typed HttpClient. Because this code
 * depends on Microsoft.Extensions.Http / DependencyInjection — a dependency NSmithy deliberately
 * does not force on plain clients — it lives in its own `{Service}.DependencyInjection.g.cs` file
 * that is only produced when the `generateDependencyInjection` plugin setting is enabled (driven by
 * the `SmithyGenerateDependencyInjection` MSBuild property). Gating generation rather than
 * compilation means the dependency-carrying file simply does not exist unless asked for.
 *
 * The extension is the one place in the DI path where protocol knowledge is available, so it
 * configures the HttpClient from modeled ALPN preferences — the raw `AddHttpClient<I,T>` path
 * cannot, because the factory owns the HttpClient.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ClientDependencyInjectionGenerator implements Runnable {

  private static final String MS_EXT_DI = "Microsoft.Extensions.DependencyInjection";

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public ClientDependencyInjectionGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    w.reserveModelNames(c.model(), c.settings());
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    // No client is generated for a service with no supported protocol, so there is nothing to
    // register. Leave an (otherwise empty) file so the output set is stable.
    List<Kind> kinds = ProtocolSupport.declaredKinds(service);
    if (kinds.isEmpty()) {
      return;
    }

    String typeName = CSharpNaming.typeName(service.getId().getName());
    String clientName = typeName + "Client";
    String interfaceName = "I" + clientName;
    String primaryProtocol = ProtocolSupport.protocolType(kinds.get(0));
    String modeledHttpVersionPreference =
        ClientGenerator.httpVersionPreferenceLiteral(
            writer,
            ProtocolSupport.httpVersionPreference(
                service,
                kinds.get(0),
                ProtocolSupport.hasEventStreamOperations(context.model(), service)));
    // Fully-qualified named-client key, unique per service.
    String namespace = context.settings().csharpNamespace(service.getId().getNamespace());
    String clientKey = (namespace.isEmpty() ? "" : namespace + ".") + clientName;

    writer.addImport(MS_EXT_DI);
    writer.addImport(RuntimeTypes.NSMITHY_CLIENT);
    writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kinds.get(0)));

    writer.write("public static class $LServiceCollectionExtensions", clientName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          // Overload 1: endpoint (turnkey).
          writer.write(
              "/// <summary>Registers <see cref=\"$L\"/> as a typed HttpClient"
                  + " (IHttpClientFactory) for the given endpoint. Set the protocol, auth schemes,"
                  + " and interceptors via <paramref name=\"configure\"/>.</summary>",
              interfaceName);
          writer.write("public static IHttpClientBuilder Add$L(", clientName);
          writer.write("    this IServiceCollection services,");
          writer.write("    " + writer.frameworkType("System.Uri") + " endpoint,");
          writer.write(
              "    " + writer.frameworkType("System.Action") + "<$LConfig>? configure = null) =>",
              clientName);
          writer.write(
              "    services.Add$L(client => client.BaseAddress = endpoint, configure);",
              clientName);
          writer.write("");

          // Overload 2: configure callbacks (Refit-style HttpClient setup + client config).
          writer.write(
              "/// <summary>Registers <see cref=\"$L\"/> as a typed HttpClient"
                  + " (IHttpClientFactory). Configure the HttpClient (at minimum its BaseAddress)"
                  + " via <paramref name=\"configureClient\"/>; set the protocol, auth schemes, and"
                  + " interceptors via <paramref name=\"configure\"/>.</summary>",
              interfaceName);
          writer.write("public static IHttpClientBuilder Add$L(", clientName);
          writer.write("    this IServiceCollection services,");
          writer.write(
              "    "
                  + writer.frameworkType("System.Action")
                  + "<"
                  + writer.frameworkType("System.Net.Http.HttpClient")
                  + ">? configureClient = null,");
          writer.write(
              "    " + writer.frameworkType("System.Action") + "<$LConfig>? configure = null)",
              clientName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    writer.frameworkType("System.ArgumentNullException")
                        + ".ThrowIfNull(services);");
                writer.write("var config = new $LConfig();", clientName);
                writer.write("configure?.Invoke(config);");
                writer.write("return services");
                writer.write("    .AddHttpClient(");
                writer.write("        $L,", CSharpNaming.formatString(clientKey));
                writer.write("        client =>");
                writer.write("        {");
                writer.write(
                    "            SmithyHttpClientEnvironment.ConfigureHttpClient(client, config,"
                        + " static () => new $L(), $L);",
                    primaryProtocol,
                    modeledHttpVersionPreference);
                writer.write("            configureClient?.Invoke(client);");
                writer.write("        })");
                writer.write(
                    "    .AddTypedClient<$L>((httpClient, _) => new $L(httpClient, config));",
                    interfaceName,
                    clientName);
              });
        });
  }
}
