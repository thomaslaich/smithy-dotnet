// Proto codegen module. Emits proto3 .proto files for Smithy services annotated
// with alloy's @grpc trait. Registered as the `proto-codegen` SmithyBuildPlugin.
//
// Intentionally standalone — no dependency on smithy-csharp-codegen — so the
// proto plugin can be swapped out independently (e.g. replaced by smithy-translate
// when it ships a proper SmithyBuildPlugin). It also acts as a test harness for
// the future NSmithy.Codecs.Protobuf codec: run both plugins against the same
// Smithy model and verify wire compatibility.

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
    publishToMavenCentral(SonatypeHost.CENTRAL_PORTAL)
    if (providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKey").isPresent) {
        signAllPublications()
    }

    configure(JavaLibrary(javadocJar = JavadocJar.Empty(), sourcesJar = true))

    coordinates(group.toString(), "smithy-proto-codegen", version.toString())

    pom {
        name.set("NSmithy smithy-proto-codegen Smithy plugin")
        description.set("Smithy build plugin that generates proto3 .proto files for services annotated with alloy's @grpc trait.")
        url.set("https://github.com/thomaslaich/smithy-dotnet")
        inceptionYear.set("2026")
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
