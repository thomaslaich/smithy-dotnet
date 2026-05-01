// Common configuration for all NSmithy codegen subprojects.
//
// This is a proof of concept exploring the official Smithy recommendation of
// implementing language code generators as Java SmithyBuildPlugin
// implementations. See codegen/README.md for the rationale and how it relates
// to docs/architecture/hybrid-codegen.md.

plugins {
    `java-library`
}

allprojects {
    group = "io.github.thomaslaich.smithy"
    version = "0.1.0-SNAPSHOT"
}

subprojects {
    apply(plugin = "java-library")

    repositories {
        mavenCentral()
    }

    extensions.configure<JavaPluginExtension> {
        toolchain {
            languageVersion.set(JavaLanguageVersion.of(21))
        }
    }

    tasks.withType<JavaCompile>().configureEach {
        options.encoding = "UTF-8"
        // Target Java 17 bytecode so the plugin loads inside the JRE bundled
        // with the Smithy CLI.
        options.release.set(17)
    }
}
