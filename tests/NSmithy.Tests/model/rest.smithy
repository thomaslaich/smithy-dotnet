$version: "2"

namespace nsmithy.tests.rest

/// Shapes the rest tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        Count
        EmptyPrefixHeaders
        QueryValues
        EnumHeaderList
        UploadUserAvatar
        GetUserAvatar
        Watch
        Upload
        Chat
        ListItems
        ListCities
    ]
}

@readonly
@http(method: "GET", uri: "/count")
operation Count {
    input := {
        @required
        @httpQuery("limit")
        limit: Integer

        @required
        @httpHeader("X-Tenant")
        tenant: String
    }
}

@readonly
@http(method: "GET", uri: "/headers")
operation EmptyPrefixHeaders {
    input := {
        @required
        @httpPrefixHeaders("")
        extraHeaders: StringMap
    }
}

map StringMap {
    key: String
    value: String
}

@readonly
@http(method: "GET", uri: "/values")
operation QueryValues {
    input := {
        @required
        @httpQuery("media")
        media: TextPlain

        @required
        @httpQuery("created")
        @timestampFormat("epoch-seconds")
        created: Timestamp
    }
}

@mediaType("text/plain")
string TextPlain

@readonly
@http(method: "GET", uri: "/statuses")
operation EnumHeaderList {
    input := {
        @required
        @httpHeader("X-Status")
        statuses: HeaderStatusList
    }
}

enum HeaderStatus {
    ACTIVE_BLUE = "ACTIVE,BLUE"
    PENDING = "PENDING"
}

list HeaderStatusList {
    member: HeaderStatus
}

@idempotent
@http(method: "PUT", uri: "/users/{userId}/avatar")
operation UploadUserAvatar {
    input := {
        @required
        @httpLabel
        userId: String

        @required
        @httpHeader("X-Checksum")
        checksum: String

        @required
        @httpPayload
        payload: AvatarStream
    }
}

@readonly
@http(method: "GET", uri: "/users/{userId}/avatar")
operation GetUserAvatar {
    input := {
        @required
        @httpLabel
        userId: String
    }

    output := {
        @required
        @httpHeader("ETag")
        eTag: String

        @required
        @httpPayload
        payload: AvatarDownload
    }
}

@streaming
@requiresLength
blob AvatarStream

@streaming
blob AvatarDownload

structure Echo {
    @required
    message: String
}

@streaming
union ChatEvents {
    message: Echo
}

@readonly
@http(method: "GET", uri: "/streams/{message}")
operation Watch {
    input := {
        @required
        @httpLabel
        message: String
    }

    output := {
        @required
        @httpHeader("X-Stream-Id")
        streamId: String

        @required
        @httpPayload
        events: ChatEvents
    }
}

@http(method: "POST", uri: "/streams/{streamId}")
operation Upload {
    input := {
        @required
        @httpLabel
        streamId: String

        @required
        @httpPayload
        events: ChatEvents
    }

    output: Echo
}

@http(method: "POST", uri: "/chat/{streamId}")
operation Chat {
    input := {
        @required
        @httpLabel
        streamId: String

        @required
        @httpPayload
        events: ChatEvents
    }

    output := {
        @required
        @httpHeader("X-Stream-Id")
        streamId: String

        @required
        @httpPayload
        events: ChatEvents
    }
}

structure ListInput {
    @httpQuery("nextToken")
    nextToken: String

    @httpQuery("pageSize")
    pageSize: Integer
}

@readonly
@http(method: "GET", uri: "/items")
operation ListItems {
    input: ListInput

    output := {
        nextToken: String
    }
}

@readonly
@http(method: "GET", uri: "/cities")
operation ListCities {
    input: ListInput
}
