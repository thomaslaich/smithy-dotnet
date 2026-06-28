{
  fetchurl,
  lib,
  stdenv,
  unzip,
}:

let
  version = "1.71.0";

  platform =
    if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isAarch64 then
      {
        name = "darwin-aarch64";
        hash = "447e1d3e08b54e1787fedd10057a259ed48954ed950f2909713de28d8ea0d3dc";
      }
    else if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isx86_64 then
      {
        name = "darwin-x86_64";
        hash = "dcda65061f51687ccded52ede603ea66381e3ca2eeaf708bdf96f93df9eb535d";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isAarch64 then
      {
        name = "linux-aarch64";
        hash = "1de152a114bcb96a31bb1b3596df5b65794a2c81e292149596e5f065330d6816";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isx86_64 then
      {
        name = "linux-x86_64";
        hash = "5bddf40fb64fd0581d85b6bdc51ae67bdc4dff0297b3db92cb800b324fa3b2ea";
      }
    else
      throw "Unsupported platform for smithy-cli: ${stdenv.hostPlatform.system}";
in
stdenv.mkDerivation {
  pname = "smithy-cli";
  inherit version;

  src = fetchurl {
    url = "https://github.com/smithy-lang/smithy/releases/download/${version}/smithy-cli-${platform.name}.zip";
    sha256 = platform.hash;
  };

  nativeBuildInputs = [ unzip ];

  dontBuild = true;

  unpackPhase = ''
    runHook preUnpack
    unzip -q "$src"
    runHook postUnpack
  '';

  installPhase = ''
    runHook preInstall

    mkdir -p "$out/share/smithy-cli" "$out/bin"
    cp -R smithy-cli-${platform.name}/. "$out/share/smithy-cli/"

    if [ -x "$out/share/smithy-cli/bin/smithy" ]; then
      ln -s "$out/share/smithy-cli/bin/smithy" "$out/bin/smithy"
    elif [ -x "$out/share/smithy-cli/smithy" ]; then
      ln -s "$out/share/smithy-cli/smithy" "$out/bin/smithy"
    else
      echo "Could not find smithy executable in Smithy CLI archive" >&2
      find "$out/share/smithy-cli" -maxdepth 3 -type f -o -type l >&2
      exit 1
    fi

    runHook postInstall
  '';

  meta = {
    description = "Command line interface for the Smithy API modeling language";
    homepage = "https://smithy.io/2.0/guides/smithy-cli/cli_installation.html";
    license = lib.licenses.asl20;
    platforms = [
      "aarch64-darwin"
      "x86_64-darwin"
      "aarch64-linux"
      "x86_64-linux"
    ];
  };
}
