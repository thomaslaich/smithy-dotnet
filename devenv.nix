{ pkgs, lib, ... }:

let
  smithy-cli = pkgs.callPackage ./nix/smithy-cli.nix { };
in
{
  packages = [
    pkgs.git
    pkgs.just
    pkgs.sbt
    pkgs.gradle
    smithy-cli
  ];

  languages.dotnet = {
    enable = true;
    # package = pkgs.dotnet-sdk_10;
    package =
      with pkgs.dotnetCorePackages;
      combinePackages [
        sdk_10_0-bin
        sdk_9_0-bin
        sdk_8_0-bin
      ];
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
          "*.MSBuild.targets"
          "Directory.Packages.props"
          "Directory.Build.props"
        ];
      };
    };
  };

  tasks."devenv:treefmt:run".exec = lib.mkForce null;
}
