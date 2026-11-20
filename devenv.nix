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
        # Only for `just smoke-templates-sdk11`; global.json keeps the repo on SDK 10.
        sdk_11_0-bin
        sdk_10_0-bin
        sdk_9_0-bin
        sdk_8_0-bin
      ];
  };

  languages.java = {
    enable = true;
    jdk.package = pkgs.jdk21;
  };

  languages.javascript.enable = true;

  env.DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  env.DOTNET_NOLOGO = "1";

  treefmt = {
    enable = true;

    config = {
      programs = {
        csharpier.enable = true;
        fantomas.enable = true;
        google-java-format.enable = true;
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
          # Golden codegen output, checked in exactly as the generators write it.
          "codegen/golden/*/csharp/**"
          "codegen/golden/*/fsharp/**"
        ];

        formatter.csharpier.includes = [
          "*.cs"
          "*.csproj"
          "*.slnx"
          "*.MSBuild.targets"
          "Directory.Packages.props"
          "Directory.Build.props"
        ];

        # treefmt-nix wraps fantomas with a .NET SDK that makes it abort on macOS; it needs none.
        formatter.fantomas.command = lib.mkForce (lib.getExe pkgs.fantomas);

        formatter.csharpier.excludes = [
          "templates/NSmithy.Templates/content/**"
          # Third-party emitter output; formatting it would mean benchmarking edits.
          "benchmarks/stacks/TypeSpec/emitted/**"
        ];
      };
    };
  };

  tasks."devenv:treefmt:run".exec = lib.mkForce null;
}
