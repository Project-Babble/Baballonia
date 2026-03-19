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
      dotnet = pkgs.dotnetCorePackages.dotnet_8;
    in {
      default = pkgs.mkShell rec {
        buildInputs = with pkgs; [
          cmake opencv icu udev
          dotnet.sdk dotnet.runtime
          xorg.libX11 xorg.libSM xorg.libICE
          libjpeg onnxruntime libGL fontconfig
          (pkgs.callPackage ./nix/opencvsharp.nix {})
        ];

        shellHook = ''
          export DOTNET_ROOT="${dotnet.sdk}"
          # Fuck knows why the runtime looks in PATH instead of LD_LIBRARY_PATH
          export PATH="$PATH:${builtins.toString (pkgs.lib.makeLibraryPath buildInputs)}";
          export LD_LIBRARY_PATH="$LD_LIBRARY_PATH:${builtins.toString (pkgs.lib.makeLibraryPath buildInputs)}";
        '';
      };
    });
  };
}

