// C# codegen module. Layout mirrors the upstream Smithy-recommended pattern,
// e.g. smithy-python/codegen/core: a thin SmithyBuildPlugin entry point that
// drives a DirectedCodegen implementation through a CodegenDirector, with
// SymbolProvider, SymbolWriter, WriterDelegator, CodegenContext and a
// SmithyIntegration SPI hook all wired up.
//
// Plugin name registered with smithy-build: `csharp-codegen`.

import com.vanniktech.maven.publish.JavaLibrary
import com.vanniktech.maven.publish.JavadocJar
import com.vanniktech.maven.publish.SourcesJar

val smithyVersion: String by project
val smithyAiTraitsVersion: String by project

plugins {
    id("com.vanniktech.maven.publish")
}

dependencies {
    api("software.amazon.smithy:smithy-codegen-core:$smithyVersion")
    api("software.amazon.smithy:smithy-model:$smithyVersion")
    api("software.amazon.smithy:smithy-build:$smithyVersion")
    api("software.amazon.smithy:smithy-utils:$smithyVersion")
    implementation("software.amazon.smithy:smithy-jsonschema:$smithyVersion")
    // Endpoint rules: the BDD and rule-set traits, BDD compilation, and the JMESPath subset of
    // @operationContextParams. smithy-aws-endpoints registers the aws.* rule functions, so models
    // using them validate wherever the C# plugin is loaded.
    implementation("software.amazon.smithy:smithy-rules-engine:$smithyVersion")
    implementation("software.amazon.smithy:smithy-jmespath:$smithyVersion")
    runtimeOnly("software.amazon.smithy:smithy-aws-endpoints:$smithyVersion")

    // Makes the official smithy.ai trait definitions and validators available whenever the
    // C# build plugin is loaded. In particular, models can use smithy.ai#prompts without copying
    // the unstable trait definition into each contracts project.
    runtimeOnly("software.amazon.smithy.java:smithy-ai-traits:$smithyAiTraitsVersion")

    testImplementation(platform("org.junit:junit-bom:6.1.3"))
    testImplementation("org.junit.jupiter:junit-jupiter-api")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.test {
    useJUnitPlatform()
}

// Generator tests define the protocol traits they use inline; the AWS trait definitions that
// smithy-aws-endpoints brings onto the plugin's runtime classpath would conflict with them.
configurations.testRuntimeClasspath {
    exclude(group = "software.amazon.smithy", module = "smithy-aws-endpoints")
    exclude(group = "software.amazon.smithy", module = "smithy-aws-traits")
}

mavenPublishing {
    // Targets the new Sonatype Central Portal (https://central.sonatype.com).
    // Requires MAVEN_CENTRAL_USERNAME / MAVEN_CENTRAL_PASSWORD secrets (user token).
    publishToMavenCentral()
    // Sign publications when a PGP key is supplied via env vars
    // ORG_GRADLE_PROJECT_signingInMemoryKey / signingInMemoryKeyPassword.
    // Skipped for local `publishToMavenLocal` runs that don't carry secrets.
    if (providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKey").isPresent) {
        signAllPublications()
    }

    configure(JavaLibrary(javadocJar = JavadocJar.Empty(), sourcesJar = SourcesJar.Sources()))

    coordinates(group.toString(), "smithy-csharp-codegen", version.toString())

    pom {
        name.set("NSmithy C# Codegen Plugin")
        description.set("Smithy build plugin that generates C# client/server code for Smithy models. Consumed by NSmithy.MSBuild via the Smithy CLI.")
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

// The AWS partition table of NSmithy.Aws, generated from the partitions.json that
// smithy-aws-endpoints ships at the Smithy version the generator builds against. `test` fails while
// the checked-in table is stale; `gradle generateAwsPartitions` rewrites it.
val awsPartitionsSource: Configuration by configurations.creating {
    isTransitive = false
}

dependencies {
    awsPartitionsSource("software.amazon.smithy:smithy-aws-endpoints:$smithyVersion")
}

val awsPartitionsFile = rootProject.layout.projectDirectory.file("../packages/NSmithy.Aws/AwsPartitions.g.cs")

fun renderAwsPartitions(): String {
    val json = zipTree(awsPartitionsSource.singleFile)
        .matching { include("**/partition/partitions.json") }
        .singleFile
        .readText()
    @Suppress("UNCHECKED_CAST")
    val partitions = (groovy.json.JsonSlurper().parseText(json) as Map<String, Any>)["partitions"]
        as List<Map<String, Any>>
    fun literal(value: Any?) = "\"" + value.toString().replace("\\", "\\\\").replace("\"", "\\\"") + "\""
    val out = StringBuilder()
    out.append("// <auto-generated>\n")
    out.append("// Generated by `gradle generateAwsPartitions` from the partitions.json of\n")
    out.append("// software.amazon.smithy:smithy-aws-endpoints:$smithyVersion. Do not edit.\n")
    out.append("// </auto-generated>\n")
    out.append("namespace NSmithy.Aws;\n\n")
    out.append("internal static partial class AwsPartitions\n{\n")
    out.append("    internal static readonly AwsPartitionDefinition[] All =\n    [\n")
    for (partition in partitions) {
        @Suppress("UNCHECKED_CAST")
        val outputs = partition["outputs"] as Map<String, Any>
        @Suppress("UNCHECKED_CAST")
        val regions = (partition["regions"] as Map<String, Any>).keys
        out.append("        new(\n")
        out.append("            ${literal(partition["id"])},\n")
        out.append("            ${literal(partition["regionRegex"])},\n")
        out.append("            [\n")
        for (region in regions) {
            out.append("                ${literal(region)},\n")
        }
        out.append("            ],\n")
        out.append("            new AwsPartition(\n")
        out.append("                ${literal(outputs["name"])},\n")
        out.append("                ${literal(outputs["dnsSuffix"])},\n")
        out.append("                ${literal(outputs["dualStackDnsSuffix"])},\n")
        out.append("                ${outputs["supportsFIPS"]},\n")
        out.append("                ${outputs["supportsDualStack"]},\n")
        out.append("                ${literal(outputs["implicitGlobalRegion"])}\n")
        out.append("            )\n")
        out.append("        ),\n")
    }
    out.append("    ];\n}\n")
    return out.toString()
}

tasks.register("generateAwsPartitions") {
    description = "Regenerates the NSmithy.Aws partition table from smithy-aws-endpoints."
    doLast { awsPartitionsFile.asFile.writeText(renderAwsPartitions()) }
}

val checkAwsPartitions by tasks.registering {
    description = "Fails when the NSmithy.Aws partition table is out of date."
    inputs.files(awsPartitionsSource)
    inputs.file(awsPartitionsFile)
    doLast {
        if (awsPartitionsFile.asFile.readText() != renderAwsPartitions()) {
            throw GradleException(
                "packages/NSmithy.Aws/AwsPartitions.g.cs is out of date; run `gradle generateAwsPartitions` in codegen.")
        }
    }
}

tasks.test {
    dependsOn(checkAwsPartitions)
}
