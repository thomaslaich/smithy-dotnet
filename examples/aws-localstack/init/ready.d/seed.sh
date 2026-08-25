#!/bin/bash
# LocalStack init hook: runs once the gateway is ready (mounted at
# /etc/localstack/init/ready.d). Seeds a little data into each service so the
# example's List* calls return populated responses — exercising the generated
# clients' response deserialization, not just the request path.
#
# `awslocal` ships in the LocalStack image and targets the local gateway with
# dummy credentials, so no AWS config is needed here. Each step tolerates an
# "already exists" error so a container restart re-running this hook is harmless.

set -uo pipefail

echo "[seed] DynamoDB tables"
for table in Books Authors; do
  awslocal dynamodb create-table \
    --table-name "$table" \
    --attribute-definitions AttributeName=Id,AttributeType=S \
    --key-schema AttributeName=Id,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST >/dev/null 2>&1 || true
done

echo "[seed] S3 buckets"
for bucket in nsmithy-demo-assets nsmithy-demo-logs; do
  awslocal s3api create-bucket --bucket "$bucket" >/dev/null 2>&1 || true
done
awslocal s3api put-object \
  --bucket nsmithy-demo-assets --key hello.txt --body /etc/hostname >/dev/null 2>&1 || true

echo "[seed] SQS queues"
for queue in nsmithy-demo-jobs nsmithy-demo-events; do
  awslocal sqs create-queue --queue-name "$queue" >/dev/null 2>&1 || true
done

echo "[seed] Lambda function"
# Build a minimal deployment package with python3 (always present in the image),
# so we don't depend on `zip` being installed. LocalStack doesn't enforce IAM,
# so a placeholder role ARN is fine.
python3 - <<'PY'
import zipfile
with zipfile.ZipFile("/tmp/greeter.zip", "w") as z:
    z.writestr("handler.py", "def handler(event, context):\n    return {'message': 'hello from nsmithy'}\n")
PY
awslocal lambda create-function \
  --function-name nsmithy-greeter \
  --runtime python3.11 \
  --handler handler.handler \
  --role arn:aws:iam::000000000000:role/lambda-role \
  --zip-file fileb:///tmp/greeter.zip >/dev/null 2>&1 || true

echo "[seed] done"
