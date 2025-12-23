{
  # These need to be the same commit as local submodules!!!!!
  inputs = {
    hypertext = {
      flake = false;
      url = "github:dfgHiatus/HyperText.Avalonia/8a16a6bcce40344ce77a386cf212ca742819c5ad";
    };

    vrcft = {
      flake = false;
      url = "github:dfgHiatus/VRCFaceTracking/46453fcda63fdcfc56663813246764fbc028d73b";
    };
  };
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs, hypertext, vrcft, ... }: let
    systems = ["x86_64-linux" "aarch64-linux" "aarch64-darwin"];
    forAllSystems = nixpkgs.lib.genAttrs systems;
    pkgsFor = system: import nixpkgs {
      inherit system;
    };
    internal = builtins.fetchurl {
      url = "http://217.154.52.44:7771/builds/trainer/1.0.0.0.zip";
      sha256 = "sha256:0cfc1r1nwcrkihmi9xn4higybyawy465qa6kpls2bjh9wbl5ys82";
    };
  in {
    packages = forAllSystems (system: let
      pkgs = pkgsFor system;
      dotnet = pkgs.dotnetCorePackages.dotnet_8;
      base = pkgs.buildDotnetModule {
        version = "0.0.0";
        pname = "baballonia";

        buildInputs = with pkgs; [
          cmake opencv udev
          libjpeg libGL fontconfig
          xorg.libX11 xorg.libSM xorg.libICE
          (pkgs.callPackage ./nix/opencvsharp.nix {})
        ];

        src = ./.;
        dotnetSdk = dotnet.sdk;
        nugetDeps = ./nix/deps.json;
        dotnetRuntime = dotnet.runtime;
        projectFile = "src/Baballonia.Desktop/Baballonia.Desktop.csproj";

        makeWrapperArgs = [
          "--chdir"
          "${placeholder "out"}/lib/baballonia"
        ];

        postUnpack = ''
          cp -r ${vrcft} $sourceRoot/src/VRCFaceTracking
          cp -r ${hypertext} $sourceRoot/src/HyperText.Avalonia
          cp ${internal} $sourceRoot/src/Baballonia.Desktop/_internal.zip

          # For some reason submodule perms get messed up
          find $sourceRoot/src -type d -exec chmod 755 {} \;
          find $sourceRoot/src -type f -exec chmod 644 {} \;
        '';

        postFixup = ''
          mkdir -p $out/lib/baballonia/Modules
          mv $out/bin/Baballonia.Desktop $out/bin/baballonia
          mv $out/lib/baballonia/Baballonia.VFTCapture.dll $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.VFTCapture.pdb $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.OpenCVCapture.dll $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.OpenCVCapture.pdb $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.IPCameraCapture.dll $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.IPCameraCapture.pdb $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.SerialCameraCapture.dll $out/lib/baballonia/Modules/
          mv $out/lib/baballonia/Baballonia.SerialCameraCapture.pdb $out/lib/baballonia/Modules/
        '';

        meta = with pkgs.lib; {
          mainProgram = "baballonia";
          platforms = platforms.linux;
          homepage = "https://github.com/Project-Babble/Baballonia";
          description = "Repo for the new Babble App, free and open source eye and face tracking for social VR";
        };
      };
    in {
      default = base.overrideAttrs (old: {
        buildInputs = old.buildInputs ++ [ pkgs.onnxruntime ];
      });
      baballonia-cuda = base.overrideAttrs (old: {
        buildInputs = old.buildInputs ++ [ pkgs.pkgsCuda.onnxruntime ];
      });
    });

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

