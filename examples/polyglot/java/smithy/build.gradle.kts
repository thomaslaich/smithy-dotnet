description = "Smithy model for the Java Hello service."

plugins {
    `java-library`
    id("software.amazon.smithy.gradle.smithy-jar")
}

dependencies {
    val smithyVersion: String by project

    api("software.amazon.smithy:smithy-aws-traits:$smithyVersion")
}

sourceSets {
    main {
        java {
            srcDir("model")
        }
    }
}
