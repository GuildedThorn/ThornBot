{
  description = "ThornBot — Discord.Net + Victoria/LavaLink bot, .NET 10";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs =
    { self, nixpkgs }:
    let
      systems = [
        "x86_64-linux"
        "aarch64-linux"
        "x86_64-darwin"
        "aarch64-darwin"
      ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = import nixpkgs { inherit system; };

          # Pinned Lavalink release the bot spawns as a child process (see
          # Services/LavaLinkService.cs). Bump the version + regenerate the hash with:
          #   nix-prefetch-url --type sha256 https://github.com/lavalink-devs/Lavalink/releases/download/<version>/Lavalink.jar
          lavalink = pkgs.fetchurl {
            url = "https://github.com/lavalink-devs/Lavalink/releases/download/4.2.2/Lavalink.jar";
            sha256 = "16hii4ypcz8mbq2gqpf37jnlwnm9f7swqwgxza4kcb07j7jh3f4c";
          };

          backend = pkgs.buildDotnetModule {
            pname = "thornbot";
            version = "1.0.0";
            src = ./.;

            projectFile = "ThornBot.csproj";

            # Regenerate with:
            #   nix build .#default.passthru.fetch-deps -o fetch-deps
            #   ./fetch-deps deps.json
            nugetDeps = ./deps.json;

            dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
            dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;

            executables = [ "ThornBot" ];
            meta.mainProgram = "ThornBot";
          };
        in
        {
          inherit lavalink;
          default = backend;
        }
      );

      nixosModules.default =
        {
          config,
          lib,
          pkgs,
          ...
        }:
        let
          cfg = config.services.thornbot;
        in
        {
          options.services.thornbot = {
            enable = lib.mkEnableOption "ThornBot Discord bot";

            package = lib.mkOption {
              type = lib.types.package;
              default = self.packages.${pkgs.stdenv.hostPlatform.system}.default;
              description = "The ThornBot package to run.";
            };

            lavalinkPackage = lib.mkOption {
              type = lib.types.package;
              default = self.packages.${pkgs.stdenv.hostPlatform.system}.lavalink;
              description = "Lavalink.jar the bot spawns for audio playback.";
            };

            javaPackage = lib.mkOption {
              type = lib.types.package;
              default = pkgs.jdk21;
              description = "JDK used to run Lavalink.";
            };

            environmentFile = lib.mkOption {
              type = lib.types.nullOr lib.types.path;
              default = null;
              description = ''
                EnvironmentFile with secrets and per-deployment settings (TOKEN,
                Discord__OwnerId, Discord__AuditChannelId, Lavalink__Authorization,
                RabbitMQ__Password, Radio__BaseUrl, ...). Use sops-nix or agenix to
                provision it. Values here override Resources/config.json via the
                standard __ hierarchical env-var binding.
              '';
            };
          };

          config = lib.mkIf cfg.enable {
            # ThornBot is a headless service host; it shouldn't serve anything
            # inbound except SSH, so lock the firewall to outbound + admin SSH.
            services.openssh = lib.mkIf (config.services.openssh.enable or false) {
              openFirewall = false;
              settings = {
                PasswordAuthentication = false;
                KbdInteractiveAuthentication = false;
              };
            };

            systemd.services.thornbot = {
              description = "ThornBot Discord bot";
              wantedBy = [ "multi-user.target" ];
              wants = [ "network-online.target" ];
              after = [ "network-online.target" ];

              # Lavalink__* aren't secrets — always point at the store paths Nix
              # built, regardless of what's in environmentFile. The authorization
              # password comes from the environmentFile (Lavalink__Authorization)
              # and is also used to bootstrap application.yml below.
              environment = {
                Lavalink__JarPath = "${cfg.lavalinkPackage}";
                Lavalink__JavaPath = "${cfg.javaPackage}/bin/java";
              };

              # Lavalink reads application.yml from its own working directory
              # (see Services/LavaLinkService.cs), which is this service's
              # WorkingDirectory — bootstrap it once from the repo's template so
              # Lavalink doesn't fall back to Spring Boot's bare default (port
              # 8080, no password). The template's placeholder
              # CHANGE_ME_youshallnotpass is replaced by the real
              # Lavalink__Authorization from the environment. Never overwritten,
              # so operator edits stick.
              preStart = ''
                if [ ! -e "$STATE_DIRECTORY/application.yml" ]; then
                  cp --no-preserve=mode,ownership ${self}/application.yml.example "$STATE_DIRECTORY/application.yml"
                  if [ -n "''${Lavalink__Authorization:-}" ]; then
                    sed -i "s|CHANGE_ME_youshallnotpass|$Lavalink__Authorization|" "$STATE_DIRECTORY/application.yml"
                  fi
                fi
              '';

              serviceConfig = {
                ExecStart = "${cfg.package}/bin/ThornBot";
                WorkingDirectory = "/var/lib/thornbot";
                StateDirectory = "thornbot";
                DynamicUser = true;
                Restart = "on-failure";
                RestartSec = 5;

                # --- Hardening (all individual knobs above opt-out) ---
                NoNewPrivileges = true;
                ProtectSystem = "strict";
                ProtectHome = true;
                PrivateTmp = true;
                PrivateDevices = true;
                ProtectKernelTunables = true;
                ProtectKernelModules = true;
                ProtectControlGroups = true;
                RestrictSUIDSGID = true;
                RestrictNamespaces = true;
                LockPersonality = true;
                MemoryDenyWriteExecute = true;
                RestrictRealtime = true;
                ProtectClock = true;
                # Clean the incoming HTTP request to the uptime push + radio API.
                CapabilityBoundingSet = [ ];
                AmbientCapabilities = [ ];
              }
              // lib.optionalAttrs (cfg.environmentFile != null) {
                EnvironmentFile = cfg.environmentFile;
              };
            };
          };
        };

      devShells = forAllSystems (
        system:
        let
          pkgs = import nixpkgs { inherit system; };
        in
        {
          default = pkgs.mkShell {
            name = "thornbot-shell";

            buildInputs = [
              pkgs.dotnetCorePackages.sdk_10_0
              pkgs.git
              pkgs.nuget
              pkgs.jdk21 # runs Lavalink locally for dev
            ];

            shellHook = ''
              echo "🚀 Entered .NET 10 dev shell (ThornBot)"
              dotnet --version
            '';
          };
        }
      );
    };
}
