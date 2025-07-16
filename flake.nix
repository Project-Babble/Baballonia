{
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs, ... }: let
    systems = ["x86_64-linux" "aarch64-linux" "aarch64-darwin"];
    forAllSystems = nixpkgs.lib.genAttrs systems;
    pkgsFor = system: import nixpkgs {
      inherit system;
    };
  in {
    devShells = forAllSystems (system: let
      pkgs = pkgsFor system;
      dotnet_sdk = pkgs.dotnet-sdk_8;
    in {
      default = pkgs.mkShell rec {
        buildInputs = with pkgs; [
          cmake opencv
          dotnet_sdk fontconfig
          libjpeg onnxruntime libGL
          xorg.libX11 xorg.libSM xorg.libICE
          (pkgs.callPackage ./nix/opencvsharp.nix {})
        ];

        shellHook = ''
          export DOTNET_ROOT="${dotnet_sdk}"
          # Fucks knows why C# looks in path instead of LD_LIBRARY_PATH but it does
          export PATH="$PATH:${builtins.toString (pkgs.lib.makeLibraryPath buildInputs)}";
          export LD_LIBRARY_PATH="$LD_LIBRARY_PATH:${builtins.toString (pkgs.lib.makeLibraryPath buildInputs)}";
        '';
      };
    });
  };
}
