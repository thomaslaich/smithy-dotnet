// C# codegen module. Layout mirrors the upstream Smithy-recommended pattern,
// e.g. smithy-python/codegen/core: a thin SmithyBuildPlugin entry point that
// drives a DirectedCodegen implementation through a CodegenDirector, with
// SymbolProvider, SymbolWriter, WriterDelegator, CodegenContext and a
// SmithyIntegration SPI hook all wired up.
//
// Plugin name registered with smithy-build: `csharp-client-codegen`.

import com.vanniktech.maven.publish.JavaLibrary
import com.vanniktech.maven.publish.JavadocJar
import com.vanniktech.maven.publish.SonatypeHost

val smithyVersion: String by project

plugins {
    id("com.vanniktech.maven.publish")
}

dependencies {
    api("software.amazon.smithy:smithy-codegen-core:$smithyVersion")
    api("software.amazon.smithy:smithy-model:$smithyVersion")
    api("software.amazon.smithy:smithy-build:$smithyVersion")
    api("software.amazon.smithy:smithy-utils:$smithyVersion")
}

mavenPublishing {
    // Targets the new Sonatype Central Portal (https://central.sonatype.com).
    // Requires MAVEN_CENTRAL_USERNAME / MAVEN_CENTRAL_PASSWORD secrets (user token).
    publishToMavenCentral(SonatypeHost.CENTRAL_PORTAL)
    // Sign publications when a PGP key is supplied via env vars
    // ORG_GRADLE_PROJECT_signingInMemoryKey / signingInMemoryKeyPassword.
    // Skipped for local `publishToMavenLocal` runs that don't carry secrets.
    if (providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKey").isPresent) {
        signAllPublications()
    }

    configure(JavaLibrary(javadocJar = JavadocJar.Empty(), sourcesJar = true))

    coordinates(group.toString(), "codegen-csharp", version.toString())

    pom {
        name.set("NSmithy csharp-codegen Smithy plugin")
        description.set("Smithy build plugin that generates C# client/server code for Smithy models. Consumed by NSmithy.MSBuild via the Smithy CLI.")
        url.set("https://github.com/thomaslaich/smithy-dotnet")
        inceptionYear.set("2025")
        licenses {
            license {
                name.set("MIT License")
                url.set("https://opensource.org/licenses/MIT")
                distribution.set("repo")
            }
        }
        developers {
            developer {
                id.set("thomaslaich")
                name.set("Thomas Laich")
                url.set("https://github.com/thomaslaich")
            }
        }
        scm {
            url.set("https://github.com/thomaslaich/smithy-dotnet")
            connection.set("scm:git:git://github.com/thomaslaich/smithy-dotnet.git")
            developerConnection.set("scm:git:ssh://git@github.com/thomaslaich/smithy-dotnet.git")
        }
    }
}
