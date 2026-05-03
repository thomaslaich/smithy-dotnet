// Common configuration for all NSmithy codegen subprojects.
//
// This is a proof of concept exploring the official Smithy recommendation of
// implementing language code generators as Java SmithyBuildPlugin
// implementations. See codegen/README.md for the rationale and how it relates
// to docs/architecture/hybrid-codegen.md.

plugins {
    `java-library`
    id("com.vanniktech.maven.publish") version "0.30.0" apply false
}

allprojects {
    group = "io.github.thomaslaich.nsmithy"
    // Default to a SNAPSHOT version for local dev (`gradle :csharp:publishToMavenLocal`).
    // The release pipeline overrides this with `-Pversion=<x.y.z>` so the published
    // Maven Central artifact carries the matching release version.
    version = (findProperty("version") as String?).takeUnless { it.isNullOrBlank() || it == "unspecified" }
        ?: "0.1.0-SNAPSHOT"
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
