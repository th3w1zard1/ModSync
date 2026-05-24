// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.Avalonia;

namespace KOTORModSync.Services
{
    public sealed class AutoUpdateSettings
    {
        public string AppCastUrl { get; set; } = "https://raw.githubusercontent.com/th3w1zard1/KOTORModSync/master/appcast.xml";
        public string SignaturePublicKey { get; set; } = "jZSQV+2C1HL2Ufek3ekC7gtgOk5ctuDQzngh86OEdlA=";
        public TimeSpan CheckFrequency { get; set; } = TimeSpan.FromHours(24);
        public bool RelaunchAfterUpdate { get; set; } = true;

        public static AutoUpdateSettings Default => new AutoUpdateSettings();
    }

    public sealed class AutoUpdateCheckResult
    {
        public static AutoUpdateCheckResult UpdateAvailableResult => new AutoUpdateCheckResult
        {
            UpdateAvailable = true,
            StatusMessage = "Update available.",
        };

        public static AutoUpdateCheckResult NoUpdateResult => new AutoUpdateCheckResult
        {
            UpdateAvailable = false,
            StatusMessage = "You are running the latest version.",
        };

        public bool UpdateAvailable { get; set; }

        public string StatusMessage { get; set; } = string.Empty;
    }

    public interface IAutoUpdateClient : IDisposable
    {
        void Initialize(AutoUpdateSettings settings);

        Task StartLoopAsync(bool doInitialCheck, TimeSpan checkFrequency, CancellationToken cancellationToken = default);

        Task<AutoUpdateCheckResult> CheckForUpdatesQuietlyAsync(CancellationToken cancellationToken = default);

        Task ShowUpdateUiAsync(CancellationToken cancellationToken = default);

        void StopLoop();
    }

    internal sealed class NetSparkleUpdateClient : IAutoUpdateClient
    {
        private SparkleUpdater _sparkle;

        public void Initialize(AutoUpdateSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!(_sparkle is null))
            {
                return;
            }

            var uiFactory = new UIFactory();
            _sparkle = new SparkleUpdater(
                appcastUrl: settings.AppCastUrl,
                signatureVerifier: new Ed25519Checker(SecurityMode.Strict, settings.SignaturePublicKey))
            {
                UIFactory = uiFactory,
                RelaunchAfterUpdate = settings.RelaunchAfterUpdate,
            };
        }

        public async Task StartLoopAsync(bool doInitialCheck, TimeSpan checkFrequency, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sparkle.StartLoop(doInitialCheck, checkFrequency).ConfigureAwait(true);
        }

        public async Task<AutoUpdateCheckResult> CheckForUpdatesQuietlyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateInfo updateInfo = await _sparkle.CheckForUpdatesQuietly().ConfigureAwait(true);
            return updateInfo.Status == UpdateStatus.UpdateAvailable
                ? AutoUpdateCheckResult.UpdateAvailableResult
                : new AutoUpdateCheckResult
                {
                    UpdateAvailable = false,
                    StatusMessage = $"No updates available. Status: {updateInfo.Status}",
                };
        }

        public async Task ShowUpdateUiAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sparkle.CheckForUpdatesAtUserRequest().ConfigureAwait(true);
        }

        public void StopLoop() => _sparkle?.StopLoop();

        internal SparkleUpdater SparkleForTests => _sparkle;

        public void Dispose()
        {
            _sparkle?.Dispose();
            _sparkle = null;
        }
    }
}
