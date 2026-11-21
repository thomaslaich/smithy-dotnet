{
  fetchurl,
  lib,
  stdenv,
  unzip,
}:

let
  version = "1.75.0";

  platform =
    if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isAarch64 then
      {
        name = "darwin-aarch64";
        hash = "c6d32931b8eaf5e93c159c870387b4e6d8c97363c8c6bb09061291c93cdc9181";
      }
    else if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isx86_64 then
      {
        name = "darwin-x86_64";
        hash = "a89ca22c941f92476fef804ac54e4d3886204da1f0554a18abd8c66073b7519e";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isAarch64 then
      {
        name = "linux-aarch64";
        hash = "8561f72386464ac8cfb0ccd3e864a59199e134f26d8ebf6bd264eebe1aa58098";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isx86_64 then
      {
        name = "linux-x86_64";
        hash = "c8d448426061586a0afb93830151838cb92a73e45d9d3733c3367ca905eb32c1";
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
