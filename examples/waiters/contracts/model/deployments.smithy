$version: "2"

namespace example.deployments

use aws.protocols#restJson1
use smithy.waiters#waitable

/// Deploys releases. A deployment runs in the background after it is created.
@restJson1
service Deployments {
    version: "2026-01-01"
    operations: [CreateDeployment, GetDeployment]
}

/// Starts a deployment and returns at once, while it is still pending.
@http(method: "POST", uri: "/deployments", code: 201)
operation CreateDeployment {
    input := {
        @required
        release: String

        /// Makes the deployment fail, to show a waiter's failure state.
        fail: Boolean = false
    }
    output := {
        @required
        deploymentId: String

        @required
        status: DeploymentStatus
    }
}

@readonly
@http(method: "GET", uri: "/deployments/{deploymentId}")
@waitable(
    DeploymentComplete: {
        documentation: "Waits until the deployment has succeeded."
        minDelay: 1
        maxDelay: 4
        acceptors: [
            {
                state: "success"
                matcher: { output: { path: "status", expected: "SUCCEEDED", comparator: "stringEquals" } }
            }
            {
                state: "failure"
                matcher: { output: { path: "status", expected: "FAILED", comparator: "stringEquals" } }
            }
            {
                state: "failure"
                matcher: { output: { path: "steps[].status", expected: "FAILED", comparator: "anyStringEquals" } }
            }
        ]
    }
)
operation GetDeployment {
    input := {
        @required
        @httpLabel
        deploymentId: String
    }
    output := {
        @required
        deploymentId: String

        @required
        status: DeploymentStatus

        steps: Steps
    }
    errors: [DeploymentNotFound]
}

list Steps {
    member: Step
}

/// One stage of a deployment.
structure Step {
    @required
    name: String

    @required
    status: DeploymentStatus
}

enum DeploymentStatus {
    PENDING
    RUNNING
    SUCCEEDED
    FAILED
}

@error("client")
@httpError(404)
structure DeploymentNotFound {
    message: String
}
