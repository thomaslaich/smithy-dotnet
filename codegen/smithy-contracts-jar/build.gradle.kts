// Contracts JAR module. Packages a projection's model as a Maven JAR + POM, so any
// Smithy toolchain can publish a contract. Standalone: no dependency on the C# codegen.

import com.vanniktech.maven.publish.JavaLibrary
import com.vanniktech.maven.publish.JavadocJar
import com.vanniktech.maven.publish.SourcesJar

val smithyVersion: String by project

plugins {
    id("com.vanniktech.maven.publish")
}

dependencies {
    api("software.amazon.smithy:smithy-model:$smithyVersion")
    api("software.amazon.smithy:smithy-build:$smithyVersion")
    api("software.amazon.smithy:smithy-utils:$smithyVersion")

    testImplementation("software.amazon.smithy:smithy-aws-traits:$smithyVersion")
    testImplementation(platform("org.junit:junit-bom:6.1.3"))
    testImplementation("org.junit.jupiter:junit-jupiter-api")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.test {
    useJUnitPlatform()
}

mavenPublishing {
    publishToMavenCentral()
    if (providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKey").isPresent) {
        signAllPublications()
    }

    configure(JavaLibrary(javadocJar = JavadocJar.Empty(), sourcesJar = SourcesJar.Sources()))

    coordinates(group.toString(), "smithy-contracts-jar", version.toString())

    pom {
        name.set("NSmithy Contracts JAR Plugin")
        description.set("Smithy build plugin that packages a Smithy model as a Maven JAR and POM for distribution.")
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
