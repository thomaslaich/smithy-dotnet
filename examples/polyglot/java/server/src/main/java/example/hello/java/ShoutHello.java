package example.hello.java;

import example.hello.java.model.ShoutHelloInput;
import example.hello.java.model.ShoutHelloOutput;
import example.hello.java.service.ShoutHelloOperation;
import java.util.Locale;
import software.amazon.smithy.java.server.RequestContext;

final class ShoutHello implements ShoutHelloOperation {
    private final String serviceName;

    ShoutHello(String serviceName) {
        this.serviceName = serviceName;
    }

    @Override
    public ShoutHelloOutput shoutHello(ShoutHelloInput input, RequestContext context) {
        return ShoutHelloOutput.builder()
                .message(("HELLO, " + input.getName() + "!").toUpperCase(Locale.ROOT))
                .from(serviceName)
                .build();
    }
}
