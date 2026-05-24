# Changelog

## [2.1.2](https://github.com/th3w1zard1/ModSync/compare/KOTORModSync-v2.1.1...KOTORModSync-v2.1.2) (2026-05-24)


### Documentation

* **plan:** mark release 2.1.1 shipped after PR [#83](https://github.com/th3w1zard1/ModSync/issues/83) ([f7bf14e](https://github.com/th3w1zard1/ModSync/commit/f7bf14eac6b5a1d131403cd3721b5e88c8ee32af))

## [2.1.1](https://github.com/th3w1zard1/ModSync/compare/KOTORModSync-v2.1.0...KOTORModSync-v2.1.1) (2026-05-24)


### Bug Fixes

* **release:** align MainConfig.CurrentVersion with manifest 2.1.0 ([#84](https://github.com/th3w1zard1/ModSync/issues/84)) ([9d5bcf1](https://github.com/th3w1zard1/ModSync/commit/9d5bcf195fa0e1b248eaddfc1667459b60f0d028))


### Documentation

* **plan:** mark PR [#84](https://github.com/th3w1zard1/ModSync/issues/84) version alignment merge shipped ([6e79ef0](https://github.com/th3w1zard1/ModSync/commit/6e79ef055fca9517143e76e59819a5de96bca494))
* **plan:** mark release 2.1.0 shipped after PR [#81](https://github.com/th3w1zard1/ModSync/issues/81) ([1940e4b](https://github.com/th3w1zard1/ModSync/commit/1940e4bc8a697f001f158d988a44fa42c23b658b))
* **plan:** set plan 009 status to shipped ([b3fd09e](https://github.com/th3w1zard1/ModSync/commit/b3fd09ee6bf07a801b2a2acee8a206f97dabc136))

## [2.1.0](https://github.com/th3w1zard1/ModSync/compare/KOTORModSync-v2.0.0...KOTORModSync-v2.1.0) (2026-05-24)


### Features

* add new methods for file enumeration and component management, enhance tests ([99c872e](https://github.com/th3w1zard1/ModSync/commit/99c872e5e8dde903576d2aa4f5803dc68eb253f1))
* categorize slow tests and enforce timeout settings for improved test management ([10bbb6b](https://github.com/th3w1zard1/ModSync/commit/10bbb6b99f32b7dece01e5496f98a81b63912707))
* enhance headless tests with new configuration methods and cleanup logic ([1ff0499](https://github.com/th3w1zard1/ModSync/commit/1ff0499d8b1f681942b850fe24ee925b644b5562))
* update project files and tests for improved HoloPatcher integration and visibility logic ([159bd1c](https://github.com/th3w1zard1/ModSync/commit/159bd1c39496b9d02be209db284cc431d5092d84))


### Bug Fixes

* add .NET Framework 4.8 compatibility fixes ([d4cbe9b](https://github.com/th3w1zard1/ModSync/commit/d4cbe9b520a9fb2f50afa42c080b85c2d8c1ba6b))
* add .NET Framework 4.8 compatibility fixes for test project ([e9d52e0](https://github.com/th3w1zard1/ModSync/commit/e9d52e018bacd095e700218813c5b890f77bcb10))
* add arm64 platform support to PostBuild 7z DLL copy target ([06debd4](https://github.com/th3w1zard1/ModSync/commit/06debd46c6a73ab105bc420332af6cb8b0a04b05))
* add arm64 platform to all csproj files ([04c620b](https://github.com/th3w1zard1/ModSync/commit/04c620bc1cebb9520d9f175cfed267835267d80a))
* add continue-on-error to test step ([77d82cd](https://github.com/th3w1zard1/ModSync/commit/77d82cd9fd80ace3fad3de68b10637e874281adb))
* add Exists check before Move operation for HoloPatcher on macOS ([bf1d4b1](https://github.com/th3w1zard1/ModSync/commit/bf1d4b1867cb42749d25577a1bd3266af7073977))
* add NetSparkle public key for signature generation ([875cc78](https://github.com/th3w1zard1/ModSync/commit/875cc782bcde22585723dba730290c839d536a42))
* add OperatingSystem.IsWindows polyfill and fix WriteAllTextAsync calls ([9378b10](https://github.com/th3w1zard1/ModSync/commit/9378b10eff1be490865c6d08fbeef5c1fa79c5aa))
* add remaining .NET Framework 4.8 compatibility fixes ([dc22c9a](https://github.com/th3w1zard1/ModSync/commit/dc22c9afb862053a78d86573feaab9b20f8812d8))
* AppCast generation git push failure ([6aaef5c](https://github.com/th3w1zard1/ModSync/commit/6aaef5cb63fc2c6822c9e1aa82c094cf37cf9acb))
* change win-arm64 framework from net48 to net8.0 ([9a990fd](https://github.com/th3w1zard1/ModSync/commit/9a990fd1af708ea27632342d0974d02421f01b71))
* **ci:** document Release Please PR permission requirement ([#80](https://github.com/th3w1zard1/ModSync/issues/80)) ([b809770](https://github.com/th3w1zard1/ModSync/commit/b809770f18b8fdc6f51d37cff8b99acd5a17438a))
* **ci:** migrate Release Please action to googleapis org ([#82](https://github.com/th3w1zard1/ModSync/issues/82)) ([65f28dc](https://github.com/th3w1zard1/ModSync/commit/65f28dc2e1bfd5ee4b35a3b7167bd325fdbd7b85))
* **ci:** restore core release build assets ([73582bc](https://github.com/th3w1zard1/ModSync/commit/73582bcb4c874f3b909d0538df1daff3aff2555f))
* **ci:** restore Release Please (remove unsupported plist extraFile) ([#79](https://github.com/th3w1zard1/ModSync/issues/79)) ([7c6477d](https://github.com/th3w1zard1/ModSync/commit/7c6477dd3e32c4da1daf7cfe6dd44f6aa6a5f838))
* clean up whitespace in build-and-release workflow ([53e13fe](https://github.com/th3w1zard1/ModSync/commit/53e13fe489a09cfb52176950ed9c21bee993119b))
* **core:** preserve dependency upgrade behavior ([1bf1e85](https://github.com/th3w1zard1/ModSync/commit/1bf1e8567797bc978ded07ae5222dbe5694d057a))
* **core:** remove direct NuGet vulnerability warnings ([6368f39](https://github.com/th3w1zard1/ModSync/commit/6368f39c50664b06b2ba29877440cf39f8135812))
* **deps:** remediate Avalonia D-Bus vulnerability (GHSA-xrw6-gwf8-vvr9) ([21a8d52](https://github.com/th3w1zard1/ModSync/commit/21a8d5263aa81bc77fd52b9570e8ae755bdf346a))
* **deps:** remediate HoloPatcher sibling Avalonia D-Bus path ([#76](https://github.com/th3w1zard1/ModSync/issues/76)) ([7b81c80](https://github.com/th3w1zard1/ModSync/commit/7b81c80746f37fcad6c3a7cb98f73a80f190b2a9))
* enhance build-and-release workflow for conditional builds ([037acd2](https://github.com/th3w1zard1/ModSync/commit/037acd2f095b2fc8e4aad698bf5cfcfc0cb797d2))
* enhance build-and-release workflow to check for existing releases ([eeb7556](https://github.com/th3w1zard1/ModSync/commit/eeb75560778942b5deddf718217c796d215f7ad8))
* improve AppCast git checkout logic to handle existing branches ([c40b899](https://github.com/th3w1zard1/ModSync/commit/c40b899545824c57d3ab0d5083ce1629e352dd20))
* improve master branch checkout process in build-and-release workflow ([92cb0f4](https://github.com/th3w1zard1/ModSync/commit/92cb0f4c3b389eef699d3a51ea782fbd9ee7cc97))
* make workflow resilient to individual build failures ([6a3311b](https://github.com/th3w1zard1/ModSync/commit/6a3311b8e8c0c8c3b4234c956a2d14fe81d5a648))
* release checkpoint handles during cleanup ([#72](https://github.com/th3w1zard1/ModSync/issues/72)) ([62d2ba4](https://github.com/th3w1zard1/ModSync/commit/62d2ba4aa9e3a5164ec42f6098410981d7fd9350))
* remove ambiguous WriteAllTextAsync overload ([7e3f0f8](https://github.com/th3w1zard1/ModSync/commit/7e3f0f8ece390fd604d341382d9b26f0c6f628e2))
* resolve CI flakiness — NuGet 401, submodule checkout, whitespace errors ([#65](https://github.com/th3w1zard1/ModSync/issues/65)) ([71269af](https://github.com/th3w1zard1/ModSync/commit/71269afa3ab88315eea8c6f17f0b8b0411daa6e5))
* **review:** apply autofix feedback ([2c53e4d](https://github.com/th3w1zard1/ModSync/commit/2c53e4dc712d41058a74b194eed0a319e03cc414))
* set PlatformTarget to match RuntimeIdentifier architecture ([6e5f62b](https://github.com/th3w1zard1/ModSync/commit/6e5f62b4133975ed98034fafacb6072e78be4384))
* simplify IsWindows polyfill to always use Environment.OSVersion ([7f1d683](https://github.com/th3w1zard1/ModSync/commit/7f1d6838e6478c734db0b4fe4f48d3841fbb5d91))
* simplify IsWindows() polyfill implementation ([42366f4](https://github.com/th3w1zard1/ModSync/commit/42366f4e11a1e73ff2fddf32e4526f0d0b66b47a))
* streamline build-and-release workflow with version variables ([2a9bdd8](https://github.com/th3w1zard1/ModSync/commit/2a9bdd89f6503bd0d19820c6008b7451a17f9b7a))
* update build-and-release workflow for .NET framework conditions ([d42ae88](https://github.com/th3w1zard1/ModSync/commit/d42ae88a4940ed20860c81a0b59dbc9de80b4ce0))
* update build-and-release workflow for additional event triggers ([e92eeb3](https://github.com/th3w1zard1/ModSync/commit/e92eeb33f8768837ecf954b5578236612f26b025))
* update build-and-release workflow for macOS package handling ([16ed7c1](https://github.com/th3w1zard1/ModSync/commit/16ed7c12f335b7380d94a8fb0cceb039e06ac86e))
* update key directory location in build-and-release workflow ([2afa6f5](https://github.com/th3w1zard1/ModSync/commit/2afa6f5e94de070a2e10a190afc24568c6bd4c46))
* update MaxBytesPerSecond comment for clarity on release speed ([cc6e809](https://github.com/th3w1zard1/ModSync/commit/cc6e809bdec0442f9a7717d729610ba4be4fb866))
* update string comparison to be case-insensitive for better key matching ([e1ad138](https://github.com/th3w1zard1/ModSync/commit/e1ad138c6d60bf560e4789fca6e4fde163119990))
* use correct dotnet restore syntax for Linux/macOS ([ff11edd](https://github.com/th3w1zard1/ModSync/commit/ff11edde0f96d9b99e947fc140b2a1fc157fb2c9))


### Documentation

* add comprehensive codebase map JSON and Master.md reference doc ([f1f264e](https://github.com/th3w1zard1/ModSync/commit/f1f264e5663191bb6dbcc81bc98e8d1165881e27))
* **plan:** mark autonomous agent guidance shipped on master ([3eedc23](https://github.com/th3w1zard1/ModSync/commit/3eedc23c2e06616196a3eaae7d209caf4907300b))
* **plan:** mark Avalonia D-Bus fix shipped on master ([97ebc28](https://github.com/th3w1zard1/ModSync/commit/97ebc28fff4e28611b6b64ce43b6784b7f0feef7))
* **plan:** mark Release Please action migration shipped after PR [#82](https://github.com/th3w1zard1/ModSync/issues/82) ([522997a](https://github.com/th3w1zard1/ModSync/commit/522997abfdaa5c20990530a3b52bc234525a41d6))
* **plan:** mark Release Please fix shipped after PR [#79](https://github.com/th3w1zard1/ModSync/issues/79) merge ([eef3484](https://github.com/th3w1zard1/ModSync/commit/eef3484ca96923f44d2b47f405fcae84844ec1d1))
* **plan:** mark Release Please PR permissions fix shipped after PR [#80](https://github.com/th3w1zard1/ModSync/issues/80) ([7f3d342](https://github.com/th3w1zard1/ModSync/commit/7f3d3426462e4a7b2e1f4d6d0fe3173b699be6ca))
* **plan:** mark ship plan shipped after PR [#77](https://github.com/th3w1zard1/ModSync/issues/77) merge ([2ac5ee6](https://github.com/th3w1zard1/ModSync/commit/2ac5ee68827f11261db6b9f61ecb9378000f6ad2))
* **plan:** record D-Bus remediation verification on master ([#77](https://github.com/th3w1zard1/ModSync/issues/77)) ([5e5df61](https://github.com/th3w1zard1/ModSync/commit/5e5df616dfdc5010318a0a5579042cf94b0f791b))
* tighten autonomous agent routing ([#71](https://github.com/th3w1zard1/ModSync/issues/71)) ([25d72a9](https://github.com/th3w1zard1/ModSync/commit/25d72a9f99fc705f7fe312c3d59469c444a5bc39))
* update AGENTS.md with project layout, build command, and Cursor Cloud instructions ([#64](https://github.com/th3w1zard1/ModSync/issues/64)) ([c49dad3](https://github.com/th3w1zard1/ModSync/commit/c49dad353137b8857ef9de10c2027466141c9ba9))
