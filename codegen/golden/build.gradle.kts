// Golden tests: generate C# and F# for each fixture model and compare the output with the files
// checked in next to it, so a pull request shows how generated code changes. Not published.
//
//   gradle :golden:test                  compare
//   gradle :golden:test -PupdateGolden   rewrite the checked-in output

val smithyVersion: String by project

dependencies {
    testImplementation(project(":smithy-csharp-codegen"))
    testImplementation(project(":smithy-csharp-aws-codegen"))
    testImplementation(project(":smithy-fsharp-codegen"))
    testImplementation("software.amazon.smithy:smithy-aws-traits:$smithyVersion")
    testImplementation("software.amazon.smithy:smithy-validation-model:$smithyVersion")
    testImplementation(platform("org.junit:junit-bom:6.1.3"))
    testImplementation("org.junit.jupiter:junit-jupiter-api")
    testRuntimeOnly("org.junit.jupiter:junit-jupiter-engine")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.test {
    useJUnitPlatform()
    // Print which files differ and how, not only that a test failed.
    testLogging {
        events("failed")
        exceptionFormat = org.gradle.api.tasks.testing.logging.TestExceptionFormat.FULL
    }
    systemProperty("nsmithy.golden.dir", projectDir.absolutePath)
    if (providers.gradleProperty("updateGolden").isPresent) {
        systemProperty("nsmithy.golden.update", "true")
        outputs.upToDateWhen { false }
    }
    // The fixtures and their expected output are inputs, so editing either reruns the test.
    inputs.files(fileTree(projectDir) { exclude("build/**", "src/**", "build.gradle.kts") })
}
