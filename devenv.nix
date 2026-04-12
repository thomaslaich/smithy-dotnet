{ pkgs, lib, ... }:

{
  packages = with pkgs; [
    git
    just
  ];

  languages.dotnet = {
    enable = true;
    package = pkgs.dotnet-sdk_10;
  };

  languages.java = {
    enable = true;
    jdk.package = pkgs.jdk21;
  };

  env.DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  env.DOTNET_NOLOGO = "1";

  treefmt = {
    enable = true;

    config = {
      programs = {
        csharpier.enable = true;
        just.enable = true;
        nixfmt.enable = true;
        yamlfmt.enable = true;
      };

      settings = {
        excludes = [
          ".devenv/**"
          ".direnv/**"
          ".git/**"
          "artifacts/**"
          "bin/**"
          "obj/**"
          "**/bin/**"
          "**/obj/**"
        ];

        formatter.csharpier.includes = [
          "*.cs"
          "*.csproj"
          "*.slnx"
        ];
      };
    };
  };

  tasks."devenv:treefmt:run".exec = lib.mkForce null;
}
