using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
#if NETFRAMEWORK
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
#endif

namespace HoloPatcher.UI
{
    /// <summary>
    /// Manages automatic updates using NetSparkle.
    /// Provides industry-standard auto-update functionality.
    /// Note: NetSparkle only supports .NET Framework, so this is a no-op for .NET Core/.NET 5+.
    /// </summary>
    public class UpdateManager : IDisposable
    {
#if NETFRAMEWORK
        private SparkleUpdater _sparkle;
#endif
        private bool _disposed = false;

        /// <summary>
        /// Gets or sets the appcast URL for stable releases.
        /// </summary>
        public string AppcastUrl { get; set; } = "https://github.com/th3w1zard1/KPatcher/releases/latest/download/appcast.xml";

        /// <summary>
        /// Gets or sets the appcast URL for beta/pre-release channel.
        /// </summary>
        public string BetaAppcastUrl { get; set; } = "https://github.com/th3w1zard1/KPatcher/releases/download/bleeding-edge/appcast-beta.xml";

        /// <summary>
        /// Gets or sets whether to use the beta channel.
        /// </summary>
        public bool UseBetaChannel { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to check for updates automatically on startup.
        /// </summary>
        public bool CheckOnStartup { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to check for updates in the background (silently).
        /// </summary>
        public bool SilentCheck { get; set; } = true;

        /// <summary>
        /// Gets or sets the Ed25519 public key for signature verification.
        /// Leave empty to use SecurityMode.Unsafe (not recommended for production).
        /// Generate keys using: netsparkle-generate-appcast --generate-keys --export true
        /// </summary>
        public string Ed25519PublicKey { get; set; } = "7ufoyE9utWpTIGQ6G5zqt3gOyzfcXniDPdlLJLun4tw=";

        /// <summary>
        /// Event raised when an update is available.
        /// </summary>
#pragma warning disable CS0067 // Event is never used - part of public API for future use
        public event EventHandler<UpdateStatusEventArgs> UpdateAvailable;

        /// <summary>
        /// Event raised when update check completes.
        /// </summary>
        public event EventHandler<UpdateStatusEventArgs> UpdateCheckFinished;
#pragma warning restore CS0067

        /// <summary>
        /// Initializes the update manager.
        /// NOTE: Should be called on the main UI thread.
        /// </summary>
        public void Initialize()
        {
#if NETFRAMEWORK
            if (_sparkle != null)
            {
                return; // Already initialized
            }

            string appcastUrl = UseBetaChannel ? BetaAppcastUrl : AppcastUrl;

            // Create signature verifier
            // For production, use Ed25519Checker with a proper public key
            // For development/testing, can use SecurityMode.Unsafe (NOT recommended for production)
            ISignatureVerifier signatureVerifier;

            if (string.IsNullOrWhiteSpace(Ed25519PublicKey))
            {
                // Use unsafe mode if no key provided (development only)
                signatureVerifier = new Ed25519Checker(SecurityMode.Unsafe, "");
            }
            else
            {
                // Use strict mode with provided public key (production)
                signatureVerifier = new Ed25519Checker(SecurityMode.Strict, Ed25519PublicKey);
            }

            // Create Sparkle updater
            _sparkle = new SparkleUpdater(appcastUrl, signatureVerifier)
            {
                // Use null for UIFactory to use default UI, or implement custom Avalonia UI factory
                UIFactory = null, // NetSparkle will use default UI if available
                RelaunchAfterUpdate = false, // Set to true if installer should restart app
                CustomInstallerArguments = "" // Add custom installer args if needed
            };

            // Subscribe to events if needed
            // Note: NetSparkle handles UI automatically, but we can listen for custom handling
#endif
        }

        /// <summary>
        /// Starts the update checking loop.
        /// NOTE: Should be called on the main UI thread.
        /// </summary>
        public void Start()
        {
#if NETFRAMEWORK
            if (_sparkle is null)
            {
                Initialize();
            }

            if (CheckOnStartup)
            {
                // Start checking for updates in the background
                // First parameter: true = check immediately, false = wait for interval
                _sparkle.StartLoop(true);
            }
#endif
        }

        /// <summary>
        /// Manually checks for updates (user-requested).
        /// </summary>
        public void CheckForUpdates()
        {
#if NETFRAMEWORK
            if (_sparkle is null)
            {
                Initialize();
            }

            _sparkle.CheckForUpdatesAtUserRequest();
#endif
        }

        /// <summary>
        /// Stops the update checking loop.
        /// </summary>
        public void Stop()
        {
#if NETFRAMEWORK
            _sparkle?.Stop();
#endif
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
#if NETFRAMEWORK
                _sparkle?.Dispose();
#endif
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for update status events.
    /// </summary>
    public class UpdateStatusEventArgs : EventArgs
    {
        public bool UpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
#if NETFRAMEWORK
        public UpdateStatus Status { get; set; } = UpdateStatus.UpdateNotAvailable;
#else
        // For .NET Core/.NET 5+, use a simple enum
        public int Status { get; set; } = 0; // 0 = UpdateNotAvailable
#endif
    }
}

