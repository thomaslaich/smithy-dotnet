import com.vanniktech.maven.publish.JavaLibrary
import com.vanniktech.maven.publish.JavadocJar
import com.vanniktech.maven.publish.SonatypeHost

val boteVersion: String by project

plugins {
    id("com.vanniktech.maven.publish")
}

dependencies {
    api(project(":smithy-csharp-codegen"))
    api("io.github.thomaslaich.bote:bote:$boteVersion")

    testImplementation("org.junit.jupiter:junit-jupiter-api:5.11.4")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine:5.11.4")
}

tasks.test {
    useJUnitPlatform()
}

base {
    archivesName = "smithy-csharp-bote-codegen"
}

mavenPublishing {
    publishToMavenCentral(SonatypeHost.CENTRAL_PORTAL)
    if (providers.environmentVariable("ORG_GRADLE_PROJECT_signingInMemoryKey").isPresent) {
        signAllPublications()
    }

    configure(JavaLibrary(javadocJar = JavadocJar.Empty(), sourcesJar = true))
    coordinates(group.toString(), "smithy-csharp-bote-codegen", version.toString())

    pom {
        name.set("NSmithy Bote C# Codegen Plugin")
        description.set("Optional Bote messaging protocol integration for the NSmithy C# generator.")
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
