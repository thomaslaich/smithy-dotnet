{ pkgs, ... }:

{
  packages = [ pkgs.awscli2 ];

  env.AWS_DEFAULT_REGION = "us-east-1";
  env.AWS_ACCESS_KEY_ID = "test";
  env.AWS_SECRET_ACCESS_KEY = "test";
  env.LOCALSTACK_ENDPOINT = "http://localhost:4566";

  # LocalStack runs in Docker (the host/CLI mode is deprecated and its nixpkgs
  # Python runtime is broken — "No module named 'rolo'"). Requires a running
  # Docker daemon; `devenv up` brings the container up via compose.yaml.
  processes.localstack.exec = "docker compose -f compose.yaml up";
}
