$version: "2"

namespace nsmithy.tests.waiters

use aws.protocols#restJson1
use smithy.waiters#waitable

@restJson1
service Tables {
    version: "2024-01-01"
    operations: [DescribeTable]
}

@readonly
@http(method: "GET", uri: "/tables/{TableName}")
@waitable(
    TableExists: {
        documentation: "Waits until the table is active."
        minDelay: 1
        maxDelay: 4
        acceptors: [
            {
                state: "success"
                matcher: { output: { path: "Table.TableStatus", expected: "ACTIVE", comparator: "stringEquals" } }
            }
            {
                state: "failure"
                matcher: { output: { path: "Table.TableStatus", expected: "FAILED", comparator: "stringEquals" } }
            }
            {
                state: "retry"
                matcher: { errorType: "ResourceNotFoundException" }
            }
        ]
    }
    TableNotExists: {
        acceptors: [
            {
                state: "success"
                matcher: { errorType: "ResourceNotFoundException" }
            }
        ]
    }
    TableGone: {
        acceptors: [
            {
                state: "success"
                matcher: { errorType: "Table.Gone" }
            }
            {
                state: "retry"
                matcher: { success: true }
            }
        ]
    }
    ReplicasActive: {
        acceptors: [
            {
                state: "success"
                matcher: { output: { path: "Table.Replicas[].Status", expected: "ACTIVE", comparator: "allStringEquals" } }
            }
            {
                state: "failure"
                matcher: { output: { path: "Table.Replicas[].Status", expected: "FAILED", comparator: "anyStringEquals" } }
            }
        ]
    }
    ReplicasFilled: {
        acceptors: [
            {
                state: "success"
                matcher: {
                    output: {
                        path: "length(Table.Replicas[?Status == 'ACTIVE']) == length(Table.Replicas) && length(Table.Replicas) > `0`"
                        expected: "true"
                        comparator: "booleanEquals"
                    }
                }
            }
        ]
    }
    ReplicasSized: {
        acceptors: [
            {
                state: "success"
                matcher: {
                    output: {
                        path: "!contains(Table.Replicas[].[Size >= MinSize][], `false`)"
                        expected: "true"
                        comparator: "booleanEquals"
                    }
                }
            }
        ]
    }
    Settled: {
        acceptors: [
            {
                state: "success"
                matcher: {
                    output: {
                        path: "Table.TableStatus != 'CREATING' && Table.TableStatus != 'UPDATING'"
                        expected: "true"
                        comparator: "booleanEquals"
                    }
                }
            }
        ]
    }
    SettingsFast: {
        acceptors: [
            {
                state: "success"
                matcher: { output: { path: "Table.Settings.*.Mode", expected: "FAST", comparator: "anyStringEquals" } }
            }
            {
                state: "failure"
                matcher: { output: { path: "Table.Settings.*.Tier", expected: "GOLD", comparator: "anyStringEquals" } }
            }
        ]
    }
    Named: {
        acceptors: [
            {
                state: "success"
                matcher: {
                    inputOutput: { path: "input.TableName == output.Table.TableName", expected: "true", comparator: "booleanEquals" }
                }
            }
        ]
    }
    Archived: {
        acceptors: [
            {
                state: "success"
                matcher: { output: { path: "Table.Archived || Table.Deleted", expected: "true", comparator: "booleanEquals" } }
            }
        ]
    }
)
operation DescribeTable {
    input := {
        @required
        @httpLabel
        TableName: String
    }
    output := {
        Table: TableDescription
    }
    errors: [ResourceNotFoundException]
}

structure TableDescription {
    TableName: String
    TableStatus: TableStatus
    Replicas: Replicas
    Settings: Settings
    Archived: Boolean
    Deleted: Boolean
}

list Replicas {
    member: Replica
}

structure Replica {
    Status: TableStatus
    Size: Integer
    MinSize: Integer
}

map Settings {
    key: String
    value: Setting
}

structure Setting {
    Mode: Mode
    Tier: Tier
}

@enum([{ value: "GOLD" }, { value: "SILVER" }])
string Tier

enum TableStatus {
    CREATING
    UPDATING
    ACTIVE
    FAILED
}

enum Mode {
    SLOW
    FAST
}

@error("client")
@httpError(404)
structure ResourceNotFoundException {
    message: String
}
