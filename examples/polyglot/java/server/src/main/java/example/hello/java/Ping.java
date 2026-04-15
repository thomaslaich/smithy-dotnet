package example.hello.java;

import example.hello.java.client.HelloServiceClient;
import example.hello.java.model.PingInput;
import example.hello.java.model.PingOutput;
import example.hello.java.model.SayHelloInput;
import example.hello.java.service.PingOperation;
import software.amazon.smithy.java.endpoints.EndpointResolver;
import software.amazon.smithy.java.server.RequestContext;

final class Ping implements PingOperation {
    @Override
    public PingOutput ping(PingInput input, RequestContext context) {
        var client = HelloServiceClient.builder()
                .endpointResolver(EndpointResolver.staticEndpoint(input.getTargetUrl()))
                .build();
        var response = client.sayHello(SayHelloInput.builder().name(input.getName()).build());
        return PingOutput.builder()
                .message(response.getMessage())
                .from(response.getFrom())
                .build();
    }
}
