// C# codegen module. Layout mirrors the upstream Smithy-recommended pattern,
// e.g. smithy-python/codegen/core: a thin SmithyBuildPlugin entry point that
// drives a DirectedCodegen implementation through a CodegenDirector, with
// SymbolProvider, SymbolWriter, WriterDelegator, CodegenContext and a
// SmithyIntegration SPI hook all wired up.
//
// Plugin name registered with smithy-build: `csharp-client-codegen`.

val smithyVersion: String by project

plugins {
    `maven-publish`
}

dependencies {
    api("software.amazon.smithy:smithy-codegen-core:$smithyVersion")
    api("software.amazon.smithy:smithy-model:$smithyVersion")
    api("software.amazon.smithy:smithy-build:$smithyVersion")
    api("software.amazon.smithy:smithy-utils:$smithyVersion")
}

publishing {
    publications {
        create<MavenPublication>("maven") {
            from(components["java"])
        }
    }
}
