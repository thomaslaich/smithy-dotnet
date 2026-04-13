{
  fetchurl,
  lib,
  stdenv,
  unzip,
}:

let
  version = "1.68.0";

  platform =
    if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isAarch64 then
      {
        name = "darwin-aarch64";
        hash = "e836bb468eb117f05597fa263864681728950e32c10a85592eb4dd643cfdee88";
      }
    else if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isx86_64 then
      {
        name = "darwin-x86_64";
        hash = "55b5e397fd42fea407326e512daf9bcd38819c03534e1243e4a0fc71a9ec5ded";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isAarch64 then
      {
        name = "linux-aarch64";
        hash = "2bbed6177b0c4fc2f75c4266a5cf72571ca35ce66da8800a90d4cf03c6bb2d42";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isx86_64 then
      {
        name = "linux-x86_64";
        hash = "ee6e6d24416b53624ba7f323628b2ca8aa67a349fbe3b2e92e98172c3f3d6a45";
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
