using System.Text;
using Microsoft.AspNetCore.Http;
using NSmithy.Server.AspNetCore;

namespace NSmithy.Tests.Server;

public sealed class SmithyAspNetCoreProtocolTests
{
    [Fact]
    public void GetRequiredQueryValueThrowsWhenMissing()
    {
        var httpContext = new DefaultHttpContext();

        var error = Assert.Throws<InvalidOperationException>(() =>
            SmithyAspNetCoreProtocol.GetRequiredQueryValue<int>(httpContext, "retries")
        );

        Assert.Equal("Missing query value 'retries'.", error.Message);
    }

    [Fact]
    public void GetRequiredHeaderValueThrowsWhenMissing()
    {
        var httpContext = new DefaultHttpContext();

        var error = Assert.Throws<InvalidOperationException>(() =>
            SmithyAspNetCoreProtocol.GetRequiredHeaderValue<string>(httpContext, "x-request-id")
        );

        Assert.Equal("Missing header value 'x-request-id'.", error.Message);
    }

    [Fact]
    public async Task ReadRequiredJsonRequestBodyAsyncThrowsWhenMissing()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream([]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SmithyAspNetCoreProtocol.ReadRequiredJsonRequestBodyAsync<string>(httpContext)
        );

        Assert.Equal("Missing JSON request body.", error.Message);
    }
}
