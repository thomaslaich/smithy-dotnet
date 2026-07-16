// Common configuration for all NSmithy codegen subprojects.
//
// This is a proof of concept exploring the official Smithy recommendation of
// implementing language code generators as Java SmithyBuildPlugin
// implementations. See codegen/README.md for the rationale and how it relates
// to docs/architecture/hybrid-codegen.md.

import net.ltgt.gradle.errorprone.errorprone

plugins {
    `java-library`
    id("com.vanniktech.maven.publish") version "0.30.0" apply false
    // Error Prone: javac-integrated static analysis for correctness bugs the
    // Java compiler does not report. Runs on every compile, locally and in CI.
    // See roadmap §4 "Improve generator clarity and diagnostics".
    id("net.ltgt.errorprone") version "4.1.0" apply false
}

allprojects {
    group = "io.github.thomaslaich.nsmithy"
    // Dev placeholder; the release pipeline overrides it with -Pversion.
    version = (findProperty("version") as String?).takeUnless { it.isNullOrBlank() || it == "unspecified" }
        ?: "0.0.0-SNAPSHOT"
}

subprojects {
    apply(plugin = "java-library")
    apply(plugin = "net.ltgt.errorprone")

    repositories {
        mavenCentral()
    }

    extensions.configure<JavaPluginExtension> {
        toolchain {
            languageVersion.set(JavaLanguageVersion.of(21))
        }
    }

    dependencies {
        "errorprone"("com.google.errorprone:error_prone_core:2.36.0")
    }

    tasks.withType<JavaCompile>().configureEach {
        options.encoding = "UTF-8"
        // Target Java 17 bytecode so the plugin loads inside the JRE bundled
        // with the Smithy CLI.
        options.release.set(17)

        options.errorprone {
            // Error Prone's default-ERROR checks (real correctness bugs) stay
            // build-breaking — that's the gate that earns its keep.

            // Dead private methods are cheap to catch and worth blocking on now
            // that the existing ones are removed.
            error("UnusedMethod")

            // StringSplitter is low-signal for this codebase: every call splits
            // on a fixed literal delimiter and iterates, where String.split's
            // trailing-empty trimming is harmless. Switching to Guava's Splitter
            // would just add a dependency for no behavioral gain.
            disable("StringSplitter")

            // UnusedVariable stays a non-blocking warning: the findings are the
            // uniformly-threaded `context` fields and `sp` parameters across the
            // generators, so removing them would fight that deliberate signature
            // pattern more than it would clean anything up.
            warn("UnusedVariable")
        }
    }

    // Don't gate test compilation on Error Prone — keep the signal focused on
    // shipped generator code.
    tasks.named<JavaCompile>("compileTestJava") {
        options.errorprone.isEnabled.set(false)
    }
}
