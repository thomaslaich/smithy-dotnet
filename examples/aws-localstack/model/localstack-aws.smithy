$version: "2"

namespace localstack.aws

use aws.auth#sigv4
use aws.protocols#awsJson1_0
use aws.protocols#awsQuery
use aws.protocols#ec2Query
use aws.protocols#restJson1
use aws.protocols#restXml

@awsJson1_0
@sigv4(name: "dynamodb")
service DynamoDB_20120810 {
    version: "2012-08-10"
    operations: [ListTables]
}

operation ListTables {
    input := {}
    output := {
        @jsonName("TableNames")
        tableNames: StringList
    }
}

@restXml
@sigv4(name: "s3")
service S3 {
    version: "2006-03-01"
    operations: [ListBuckets]
}

@http(method: "GET", uri: "/")
@readonly
operation ListBuckets {
    output: ListBucketsOutput
}

@xmlName("ListAllMyBucketsResult")
@xmlNamespace(uri: "http://s3.amazonaws.com/doc/2006-03-01/")
structure ListBucketsOutput {
    @xmlName("Owner")
    owner: S3Owner

    @xmlName("Buckets")
    buckets: S3BucketList
}

structure S3Owner {
    @xmlName("ID")
    id: String

    @xmlName("DisplayName")
    displayName: String
}

list S3BucketList {
    @xmlName("Bucket")
    member: S3Bucket
}

structure S3Bucket {
    @xmlName("Name")
    name: String

    @xmlName("CreationDate")
    creationDate: String
}

@restJson1
@sigv4(name: "lambda")
service Lambda {
    version: "2015-03-31"
    operations: [ListFunctions]
}

@awsQuery
@sigv4(name: "sqs")
@xmlNamespace(uri: "http://queue.amazonaws.com/doc/2012-11-05/")
service SQS {
    version: "2012-11-05"
    operations: [ListQueues]
}

operation ListQueues {
    input := {}
    output := {
        @xmlFlattened
        @xmlName("QueueUrl")
        queueUrls: QueueUrlList
    }
}

list QueueUrlList {
    member: String
}

@ec2Query
@sigv4(name: "ec2")
@xmlNamespace(uri: "http://ec2.amazonaws.com/doc/2016-11-15/")
service EC2 {
    version: "2016-11-15"
    operations: [DescribeRegions]
}

operation DescribeRegions {
    input := {}
    output := {
        @xmlName("regionInfo")
        regions: RegionList
    }
}

list RegionList {
    @xmlName("item")
    member: Region
}

structure Region {
    @xmlName("regionName")
    name: String

    @xmlName("regionEndpoint")
    endpoint: String
}

@http(method: "GET", uri: "/2015-03-31/functions/")
@readonly
operation ListFunctions {
    output := {
        @jsonName("Functions")
        functions: FunctionList
    }
}

list FunctionList {
    member: FunctionConfiguration
}

structure FunctionConfiguration {
    @jsonName("FunctionName")
    functionName: String

    @jsonName("Runtime")
    runtime: String
}

list StringList {
    member: String
}
