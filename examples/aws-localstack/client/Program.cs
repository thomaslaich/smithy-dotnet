using Localstack.Aws;
using NSmithy.Aws;

var endpoint = new Uri(
    Environment.GetEnvironmentVariable("LOCALSTACK_ENDPOINT") ?? "http://localhost:4566"
);
var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
var credentials = new StaticAwsCredentialsProvider(new AwsCredentials("test", "test"));

using var dynamoDb = new DynamoDB20120810Client(
    endpoint,
    new() { AuthSchemes = { new AwsSigV4AuthScheme("dynamodb", region, credentials) } }
);
var tables = await dynamoDb.ListTablesAsync(new ListTablesInput());
var tableNames = tables.TableNames?.Values ?? [];
Console.WriteLine(
    $"AWS JSON / DynamoDB ListTables: {tableNames.Count} table(s) [{string.Join(", ", tableNames)}]"
);

using var s3 = new S3Client(
    endpoint,
    new() { AuthSchemes = { new AwsSigV4AuthScheme("s3", region, credentials) } }
);
var buckets = await s3.ListBucketsAsync(new ListBucketsInput());
var bucketNames = buckets.Buckets?.Values.Select(b => b.Name) ?? [];
Console.WriteLine(
    $"restXml / S3 ListBuckets: {buckets.Buckets?.Values.Count ?? 0} bucket(s) [{string.Join(", ", bucketNames)}]"
);

using var lambda = new LambdaClient(
    endpoint,
    new() { AuthSchemes = { new AwsSigV4AuthScheme("lambda", region, credentials) } }
);
var functions = await lambda.ListFunctionsAsync(new ListFunctionsInput());
var functionNames = functions.Functions?.Values.Select(f => f.FunctionName) ?? [];
Console.WriteLine(
    $"restJson1 / Lambda ListFunctions: {functions.Functions?.Values.Count ?? 0} function(s) [{string.Join(", ", functionNames)}]"
);
