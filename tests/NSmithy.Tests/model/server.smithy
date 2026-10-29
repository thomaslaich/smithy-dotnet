$version: "2"

namespace nsmithy.tests.server

/// Shapes the server tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        CreateUser
        RenameUser
    ]
}

@http(method: "POST", uri: "/users")
operation CreateUser {
    input := {
        @required
        @length(min: 3)
        name: String
    }

    output := {
        @required
        name: String
    }

    errors: [
        UserExists
    ]
}

/// Wire names differ from member names, so validation paths show which of the two they use.
@http(method: "POST", uri: "/users/rename")
operation RenameUser {
    input := {
        @required
        @jsonName("display_name")
        @length(min: 3)
        displayName: String

        @jsonName("contact_info")
        contact: Contact
    }

    output := {
        @required
        displayName: String
    }
}

structure Contact {
    @required
    @jsonName("e_mail")
    email: String
}

@error("client")
@httpError(409)
structure UserExists {
    message: String
}
