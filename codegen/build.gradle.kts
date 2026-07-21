// Common configuration for all NSmithy codegen subprojects.
//
// This is a proof of concept exploring the official Smithy recommendation of
// implementing language code generators as Java SmithyBuildPlugin
// implementations. See codegen/README.md for the rationale and how it relates
// to docs/architecture/hybrid-codegen.md.

import java.io.File
import java.security.MessageDigest
import net.ltgt.gradle.errorprone.errorprone
import org.gradle.api.artifacts.component.ModuleComponentIdentifier
import org.gradle.api.artifacts.result.ResolvedArtifactResult
import org.gradle.maven.MavenModule
import org.gradle.maven.MavenPomArtifact

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

// ============================================================================
// Bundled Maven repo for offline codegen (consumed by NSmithy.MSBuild).
//
// Assembles the codegen toolchain (smithy-csharp-codegen + smithy-proto-codegen)
// and the first-party Smithy trait packages our protocols need, plus their full
// runtime closure (JARs *and* POMs), into a Maven-layout directory. NSmithy.MSBuild
// points the Smithy CLI at it via SMITHY_MAVEN_REPOS, so `smithy build` resolves the
// codegen plugin and model traits from local files — Smithy's own offline mechanism,
// which (unlike a bare classpath) loads the build plugin correctly.
//
//   gradle :bundleMavenRepo   -> codegen/build/maven-bundle
// ============================================================================
repositories {
    mavenCentral()
    mavenLocal() // our plugins are resolved here (published just below)
}

val codegenBundle: Configuration by configurations.creating

dependencies {
    val smithyVer = property("smithyVersion") as String
    codegenBundle("io.github.thomaslaich.nsmithy:smithy-csharp-codegen:${project.version}")
    codegenBundle("io.github.thomaslaich.nsmithy:smithy-proto-codegen:${project.version}")
    codegenBundle("software.amazon.smithy:smithy-aws-traits:$smithyVer")
    codegenBundle("software.amazon.smithy:smithy-protocol-traits:$smithyVer")
    codegenBundle("software.amazon.smithy:smithy-docgen:$smithyVer")
    codegenBundle("software.amazon.smithy:smithy-openapi:$smithyVer")
    codegenBundle("com.disneystreaming.alloy:alloy-core:0.3.38")
}

tasks.register("bundleMavenRepo") {
    // Resolve our plugins from ~/.m2 at the current version, so the bundle never
    // depends on a Maven Central publish having propagated.
    dependsOn(":smithy-csharp-codegen:publishToMavenLocal", ":smithy-proto-codegen:publishToMavenLocal")
    val conf = codegenBundle
    val outDir = layout.buildDirectory.dir("maven-bundle")
    inputs.files(conf)
    outputs.dir(outDir)
    doLast {
        val repo = outDir.get().asFile
        repo.deleteRecursively()
        repo.mkdirs()

        fun artifactDir(group: String, name: String, ver: String) =
            repo.resolve("${group.replace('.', '/')}/$name/$ver").apply { mkdirs() }

        // Artifact JARs.
        conf.resolvedConfiguration.resolvedArtifacts.forEach { art ->
            val id = art.moduleVersion.id
            val classifier = art.classifier?.let { "-$it" } ?: ""
            art.file.copyTo(
                artifactDir(id.group, id.name, id.version)
                    .resolve("${id.name}-${id.version}$classifier.${art.extension ?: "jar"}"),
                overwrite = true,
            )
        }

        // POMs — the resolver reads them to reconstruct the dependency graph.
        val componentIds = conf.incoming.resolutionResult.allComponents
            .mapNotNull { it.id as? ModuleComponentIdentifier }
        val pomResult = dependencies.createArtifactResolutionQuery()
            .forComponents(componentIds)
            .withArtifacts(MavenModule::class.java, MavenPomArtifact::class.java)
            .execute()
        pomResult.resolvedComponents.forEach { comp ->
            val id = comp.id as? ModuleComponentIdentifier ?: return@forEach
            comp.getArtifacts(MavenPomArtifact::class.java).forEach { r ->
                if (r is ResolvedArtifactResult) {
                    r.file.copyTo(
                        artifactDir(id.group, id.module, id.version)
                            .resolve("${id.module}-${id.version}.pom"),
                        overwrite = true,
                    )
                }
            }
        }
        // SHA-1 checksums alongside each artifact so the resolver doesn't warn about
        // being unable to verify integrity.
        repo.walkTopDown()
            .filter { it.isFile && (it.extension == "jar" || it.extension == "pom") }
            .forEach { f ->
                val hex = MessageDigest.getInstance("SHA-1")
                    .digest(f.readBytes())
                    .joinToString("") { b -> "%02x".format(b.toInt() and 0xff) }
                File(f.parentFile, "${f.name}.sha1").writeText(hex)
            }

        logger.lifecycle("Bundled Maven repo -> ${repo.absolutePath}")
    }
}
