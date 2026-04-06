// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using Avalonia.Data.Converters;

using JetBrains.Annotations;

using KOTORModSync.Core;
using KOTORModSync.Core.Utility;

namespace KOTORModSync.Converters
{
    public partial class OpenLinkConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (value is string url)
            {
                OpenLink(url);
            }

            return null;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        ) => throw new NotImplementedException();

        private static void OpenLink([NotNull] string url)
        {
            try
            {
                if (url is null)
                {
                    throw new ArgumentNullException(nameof(url));
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    throw new ArgumentException("Invalid URL");
                }

                string scheme = uri.Scheme;
                if (!string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(scheme, "mailto", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning($"Refusing to open link with disallowed scheme '{scheme}'.");
                    return;
                }

                string launchUrl = uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);

                OSPlatform runningOs = UtilityHelper.GetOperatingSystem();
                if (runningOs == OSPlatform.Windows)
                {
                    _ = Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = launchUrl,
                            UseShellExecute = true,
                        }
                    );
                }
                else if (runningOs == OSPlatform.OSX)
                {
                    _ = Process.Start(fileName: "open", launchUrl);
                }
                else if (runningOs == OSPlatform.Linux)
                {
                    _ = Process.Start(fileName: "xdg-open", launchUrl);
                }
                else
                {
                    Logger.LogError("Unsupported platform, cannot open link.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to open URL: {ex.Message}");
            }
        }
    }
}
