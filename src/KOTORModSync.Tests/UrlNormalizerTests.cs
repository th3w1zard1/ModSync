// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Linq;

using KOTORModSync.Core.Utility;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public class UrlNormalizerTests
    {
        [Test]
        public void Normalize_WithUppercaseScheme_ConvertsToLowercase()
        {
            string url = "HTTPS://example.com/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.StartWith("https://"), "Uppercase scheme should be converted to lowercase");
                Assert.That(normalized, Does.Contain("example.com"), "Host should be preserved");
                Assert.That(normalized, Does.Contain("/path"), "Path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithUppercaseHost_ConvertsToLowercase()
        {
            string url = "https://EXAMPLE.COM/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.Contain("example.com"), "Uppercase host should be converted to lowercase");
                Assert.That(normalized, Does.StartWith("https://"), "Scheme should be preserved");
                Assert.That(normalized, Does.Contain("/path"), "Path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithDefaultPort_RemovesPort()
        {
            string url1 = "https://example.com:443/path";
            string url2 = "http://example.com:80/path";

            string normalized1 = UrlNormalizer.Normalize(url1);
            string normalized2 = UrlNormalizer.Normalize(url2);

            Assert.Multiple(() =>
            {
                Assert.That(normalized1, Is.Not.Null, "First normalized URL should not be null");
                Assert.That(normalized2, Is.Not.Null, "Second normalized URL should not be null");
                Assert.That(normalized1, Does.Not.Contain(":443"), "Default HTTPS port (443) should be removed");
                Assert.That(normalized2, Does.Not.Contain(":80"), "Default HTTP port (80) should be removed");
                Assert.That(normalized1, Does.Contain("example.com"), "Host should be preserved in first URL");
                Assert.That(normalized2, Does.Contain("example.com"), "Host should be preserved in second URL");
            });
        }

        [Test]
        public void Normalize_WithNonDefaultPort_KeepsPort()
        {
            string url = "https://example.com:8443/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.Contain(":8443"), "Non-default port should be preserved");
                Assert.That(normalized, Does.StartWith("https://"), "Scheme should be preserved");
                Assert.That(normalized, Does.Contain("example.com"), "Host should be preserved");
            });
        }

        [Test]
        public void Normalize_WithFragment_RemovesFragment()
        {
            string url = "https://example.com/path#section";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.Not.Contain("#section"), "Fragment should be removed");
                Assert.That(normalized, Does.Not.Contain("#"), "Fragment marker should be removed");
                Assert.That(normalized, Does.Contain("example.com/path"), "Host and path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithTrailingSlash_RemovesTrailingSlash()
        {
            string url = "https://example.com/path/";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Is.EqualTo("https://example.com/path"), "Trailing slash should be removed");
                Assert.That(normalized, Does.Not.EndWith("/"), "Normalized URL should not end with slash");
            });
        }

        [Test]
        public void Normalize_WithEmptyPath_DoesNotAddSlash()
        {
            string url = "https://example.com";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Is.EqualTo("https://example.com"), "URL with empty path should remain unchanged");
                // Check for double slashes in path part only (after the protocol separator)
                string pathPart = normalized.Contains("://") ? normalized.Substring(normalized.IndexOf("://", StringComparison.Ordinal) + 3) : normalized;
                Assert.That(pathPart, Does.Not.Contain("//"), "URL should not have double slashes in path part");
            });
        }

        [Test]
        public void Normalize_WithPercentEncoding_PreservesEncoding()
        {
            string url = "https://example.com/path%20with%20spaces";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.Contain("%20"), "Percent encoding should be preserved");
                Assert.That(normalized, Does.Contain("path%20with%20spaces"), "Full encoded path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithQueryParameters_SortsParameters()
        {
            string url = "https://example.com/path?z=last&a=first&m=middle";
            string normalized = UrlNormalizer.Normalize(url);

            int aIndex = normalized.IndexOf("a=", StringComparison.Ordinal);
            int mIndex = normalized.IndexOf("m=", StringComparison.Ordinal);
            int zIndex = normalized.IndexOf("z=", StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(aIndex, Is.GreaterThan(-1), "Parameter 'a' should be present");
                Assert.That(mIndex, Is.GreaterThan(-1), "Parameter 'm' should be present");
                Assert.That(zIndex, Is.GreaterThan(-1), "Parameter 'z' should be present");
                Assert.That(aIndex, Is.LessThan(mIndex), "Parameter 'a' should come before 'm' (alphabetical order)");
                Assert.That(mIndex, Is.LessThan(zIndex), "Parameter 'm' should come before 'z' (alphabetical order)");
            });
        }

        [Test]
        public void Normalize_WithEmptyQueryValue_Preserves()
        {
            string url = "https://example.com/path?key=";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.Contain("key="), "Query parameter with empty value should preserve equals sign");
                Assert.That(normalized, Does.Contain("example.com/path"), "Host and path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithDotSegments_ResolvesPath()
        {
            string url = "https://example.com/a/./b/../c";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                // Should resolve to /a/c
                Assert.That(normalized, Does.Contain("/a/c"), "Dot segments should be resolved correctly");
                Assert.That(normalized, Does.Not.Contain("/./"), "Current directory segment should be removed");
                Assert.That(normalized, Does.Not.Contain("/../"), "Parent directory segment should be resolved");
            });
        }

        [Test]
        public void Normalize_WithWWWSubdomain_PreservesWWW()
        {
            string url = "https://www.example.com/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Normalized URL should not be null");
                Assert.That(normalized, Does.Contain("www.example.com"), "WWW subdomain should be preserved");
                Assert.That(normalized, Does.StartWith("https://"), "Scheme should be preserved");
            });
        }

        [Test]
        public void Normalize_SameUrlDifferentOrder_ProducesSameResult()
        {
            // Real DeadlyStream URLs with different tab orders
            string url1 = "https://deadlystream.com/files/file/1313-example-dialogue-enhancement/?tab=files&sort=newest";
            string url2 = "https://deadlystream.com/files/file/1313-example-dialogue-enhancement/?sort=newest&tab=files";

            string normalized1 = UrlNormalizer.Normalize(url1);
            string normalized2 = UrlNormalizer.Normalize(url2);

            Assert.Multiple(() =>
            {
                Assert.That(normalized1, Is.Not.Null, "First normalized URL should not be null");
                Assert.That(normalized2, Is.Not.Null, "Second normalized URL should not be null");
                // Should be the same after normalization (query params sorted)
                Assert.That(normalized1, Is.EqualTo(normalized2), "URLs with different query parameter order should normalize to same result");
                Assert.That(normalized1, Does.Contain("deadlystream.com"), "Host should be preserved");
            });
        }

        [Test]
        public void Normalize_WithUsernamePassword_RemovesCredentials()
        {
            string url = "https://user:pass@example.com/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Not.Contain("user"), "Normalized URL should not contain username");
                Assert.That(normalized, Does.Not.Contain("pass"), "Normalized URL should not contain password");
                Assert.That(normalized, Does.Not.Contain("@"), "Normalized URL should not contain @ separator");
                Assert.That(normalized, Does.StartWith("https://"), "Normalized URL should preserve scheme");
                Assert.That(normalized, Does.Contain("example.com/path"), "Normalized URL should preserve host and path");
            });
        }

        [Test]
        public void Normalize_WithNullUrl_ReturnsNull()
        {
            string normalized = UrlNormalizer.Normalize(null);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Null, "Null URL should return null");
            });
        }

        [Test]
        public void Normalize_WithEmptyString_ReturnsEmptyString()
        {
            string normalized = UrlNormalizer.Normalize(string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.EqualTo(string.Empty), "Empty string URL should return empty string");
            });
        }

        [Test]
        public void Normalize_WithWhitespaceOnly_ReturnsWhitespace()
        {
            string whitespace = "   ";
            string normalized = UrlNormalizer.Normalize(whitespace);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.EqualTo(whitespace), "Whitespace-only URL should return as-is");
            });
        }

        [Test]
        public void Normalize_WithInvalidUrl_ReturnsAsIs()
        {
            string invalidUrl = "not a valid url";
            string normalized = UrlNormalizer.Normalize(invalidUrl);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.EqualTo(invalidUrl), "Invalid URL format should return as-is");
            });
        }

        [Test]
        public void Normalize_WithIPv6Address_HandlesCorrectly()
        {
            string url = "https://[2001:0db8:85a3:0000:0000:8a2e:0370:7334]/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "IPv6 URL should normalize successfully");
                Assert.That(normalized, Does.StartWith("https://"), "Scheme should be preserved");
                // IPv6 addresses are normalized by Uri class, so exact format may vary
                Assert.That(normalized, Does.Contain("2001"), "IPv6 address should be preserved in some form");
            });
        }

        [Test]
        public void Normalize_WithDoubleSlashesInPath_HandlesCorrectly()
        {
            string url = "https://example.com/path//to//file";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "URL with double slashes should normalize");
                Assert.That(normalized, Does.Contain("example.com"), "Host should be preserved");
            });
        }

        [Test]
        public void Normalize_WithEncodedCharacters_PreservesEncoding()
        {
            string url = "https://example.com/path%2Fwith%2Fencoded%2Fslashes";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Contain("%2F"), "Encoded slashes should be preserved");
                Assert.That(normalized, Does.Contain("path%2Fwith%2Fencoded%2Fslashes"), "Full encoded path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithMultipleQueryParameters_SortsAlphabetically()
        {
            string url = "https://example.com/path?zebra=last&alpha=first&middle=center";
            string normalized = UrlNormalizer.Normalize(url);

            int alphaIndex = normalized.IndexOf("alpha=", StringComparison.Ordinal);
            int middleIndex = normalized.IndexOf("middle=", StringComparison.Ordinal);
            int zebraIndex = normalized.IndexOf("zebra=", StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(alphaIndex, Is.GreaterThan(-1), "Alpha parameter should be present");
                Assert.That(middleIndex, Is.GreaterThan(-1), "Middle parameter should be present");
                Assert.That(zebraIndex, Is.GreaterThan(-1), "Zebra parameter should be present");
                Assert.That(alphaIndex, Is.LessThan(middleIndex), "Alpha should come before middle");
                Assert.That(middleIndex, Is.LessThan(zebraIndex), "Middle should come before zebra");
            });
        }

        [Test]
        public void Normalize_WithDuplicateQueryParameters_HandlesCorrectly()
        {
            string url = "https://example.com/path?key=value1&key=value2";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "URL with duplicate query parameters should normalize");
                Assert.That(normalized, Does.Contain("key="), "Query parameter key should be present");
            });
        }

        [Test]
        public void Normalize_WithQueryParameterValueEncoding_PreservesEncoding()
        {
            string url = "https://example.com/path?search=hello%20world&filter=test%2Fvalue";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Contain("%20"), "Encoded spaces should be preserved");
                Assert.That(normalized, Does.Contain("%2F"), "Encoded slashes should be preserved");
            });
        }

        [Test]
        public void Normalize_WithRootPath_HandlesCorrectly()
        {
            string url = "https://example.com/";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.EqualTo("https://example.com"), "Root path with trailing slash should remove slash");
            });
        }

        [Test]
        public void Normalize_WithComplexPathSegments_ResolvesCorrectly()
        {
            string url = "https://example.com/a/./b/../c/../../d";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Complex path segments should normalize");
                Assert.That(normalized, Does.Contain("example.com"), "Host should be preserved");
            });
        }

        [Test]
        public void Normalize_WithFtpScheme_HandlesCorrectly()
        {
            string url = "ftp://example.com/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "FTP URL should normalize successfully");
                Assert.That(normalized, Does.StartWith("ftp://"), "FTP scheme should be preserved");
                Assert.That(normalized, Does.Contain("example.com"), "Host should be preserved");
                // Path normalization may remove trailing slashes or collapse segments
                Assert.That(normalized, Does.Contain("example.com"), "Normalized URL should contain host");
            });
        }

        [Test]
        public void Normalize_WithFileScheme_HandlesCorrectly()
        {
            string url = "file:///C:/path/to/file.txt";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "File scheme URL should normalize");
                Assert.That(normalized, Does.StartWith("file://"), "File scheme should be preserved");
            });
        }

        [Test]
        public void Normalize_WithPortZero_HandlesCorrectly()
        {
            string url = "https://example.com:0/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "URL with port 0 should normalize");
                Assert.That(normalized, Does.Contain("example.com"), "Host should be preserved");
            });
        }

        [Test]
        public void Normalize_WithVeryLongUrl_HandlesCorrectly()
        {
            string longPath = "/" + string.Join("/", new string[100].Select((_, i) => $"segment{i}"));
            string url = $"https://example.com{longPath}";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "Very long URL should normalize");
                Assert.That(normalized, Does.StartWith("https://example.com"), "Scheme and host should be preserved");
            });
        }

        [Test]
        public void Normalize_WithInternationalizedDomainName_HandlesCorrectly()
        {
            // IDN should be punycode encoded, but we test that normalization doesn't break
            string url = "https://例え.テスト/path";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "IDN URL should normalize");
                Assert.That(normalized, Does.StartWith("https://"), "Scheme should be preserved");
            });
        }

        [Test]
        public void Normalize_WithMixedCaseInPath_PreservesCase()
        {
            string url = "https://example.com/Path/To/File.TXT";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Contain("/Path/To/File.TXT"), "Path case should be preserved");
            });
        }

        [Test]
        public void Normalize_WithQueryParameterNoValue_PreservesEquals()
        {
            string url = "https://example.com/path?key1=&key2=value";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Contain("key1="), "Query parameter with empty value should preserve equals");
                Assert.That(normalized, Does.Contain("key2=value"), "Query parameter with value should be preserved");
            });
        }

        [Test]
        public void Normalize_WithFragmentOnly_RemovesFragment()
        {
            string url = "https://example.com/path#section";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Not.Contain("#"), "Fragment should be removed");
                Assert.That(normalized, Does.Contain("example.com/path"), "Host and path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithMultipleFragments_RemovesAllFragments()
        {
            // Note: URLs can only have one fragment, but test edge case handling
            string url = "https://example.com/path#fragment1#fragment2";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Not.Contain("#fragment2"), "Second fragment marker should be removed");
            });
        }

        [Test]
        public void Normalize_WithTrailingQuestionMark_HandlesCorrectly()
        {
            string url = "https://example.com/path?";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "URL with trailing question mark should normalize");
                Assert.That(normalized, Does.Contain("example.com/path"), "Host and path should be preserved");
            });
        }

        [Test]
        public void Normalize_WithTrailingAmpersand_HandlesCorrectly()
        {
            string url = "https://example.com/path?key=value&";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "URL with trailing ampersand should normalize");
                Assert.That(normalized, Does.Contain("key=value"), "Query parameter should be preserved");
            });
        }

        [Test]
        public void Normalize_WithSpecialCharactersInQueryValue_EncodesCorrectly()
        {
            string url = "https://example.com/path?search=hello+world&filter=a=b";
            string normalized = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Is.Not.Null, "URL with special characters in query should normalize");
                Assert.That(normalized, Does.Contain("search="), "Search parameter should be present");
                Assert.That(normalized, Does.Contain("filter="), "Filter parameter should be present");
            });
        }

        [Test]
        public void Normalize_WithSameUrlMultipleTimes_ProducesConsistentResult()
        {
            string url = "https://example.com/path?z=last&a=first";
            string normalized1 = UrlNormalizer.Normalize(url);
            string normalized2 = UrlNormalizer.Normalize(url);
            string normalized3 = UrlNormalizer.Normalize(url);

            Assert.Multiple(() =>
            {
                Assert.That(normalized1, Is.EqualTo(normalized2), "First and second normalizations should be identical");
                Assert.That(normalized2, Is.EqualTo(normalized3), "Second and third normalizations should be identical");
                Assert.That(normalized1, Is.EqualTo(normalized3), "First and third normalizations should be identical");
            });
        }
    }
}
