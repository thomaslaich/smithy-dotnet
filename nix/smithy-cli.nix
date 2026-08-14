{
  fetchurl,
  lib,
  stdenv,
  unzip,
}:

let
  version = "1.73.0";

  platform =
    if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isAarch64 then
      {
        name = "darwin-aarch64";
        hash = "daf789553a20822138bc90b913233374613e1a4515a61358241d5c5489be0be9";
      }
    else if stdenv.hostPlatform.isDarwin && stdenv.hostPlatform.isx86_64 then
      {
        name = "darwin-x86_64";
        hash = "eb6f7e72245ecf0e3df992314c80dde080e4215716214874a0c3b94f9813562f";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isAarch64 then
      {
        name = "linux-aarch64";
        hash = "f69295411846274b9e8128f31ffa1d7ad02fa078047e2c4e46d5d85bcba4fc20";
      }
    else if stdenv.hostPlatform.isLinux && stdenv.hostPlatform.isx86_64 then
      {
        name = "linux-x86_64";
        hash = "9071a7db052da81ab6f4be1b4d43ea152b44b78217be0dd21d37d9ea5ec1942d";
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
