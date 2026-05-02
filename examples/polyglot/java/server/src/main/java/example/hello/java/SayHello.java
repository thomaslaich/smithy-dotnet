package example.hello.java;

import example.hello.java.model.SayHelloInput;
import example.hello.java.model.SayHelloOutput;
import example.hello.java.service.SayHelloOperation;
import software.amazon.smithy.java.server.RequestContext;

final class SayHello implements SayHelloOperation {
  private final String serviceName;

  SayHello(String serviceName) {
    this.serviceName = serviceName;
  }

  @Override
  public SayHelloOutput sayHello(SayHelloInput input, RequestContext context) {
    return SayHelloOutput.builder()
        .message("Hello, " + input.getName() + "!")
        .from(serviceName)
        .build();
  }
}
