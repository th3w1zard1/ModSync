// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using KOTORModSync.Converters;
using KOTORModSync.Services;

using NUnit.Framework;

namespace KOTORModSync.Tests
{
    [TestFixture]
    public sealed class MarkdownRendererPlainTextTests
    {
        [Test]
        public void MarkdownToPlainText_Link_KeepsLinkTextOnly()
        {
            string result = MarkdownRenderer.MarkdownToPlainText("See [docs](https://example.com) here.");
            Assert.That(result, Is.EqualTo("See docs here."));
        }

        [Test]
        public void MarkdownToPlainText_BoldAndItalic_StripsMarkers()
        {
            string result = MarkdownRenderer.MarkdownToPlainText("**bold** and *italic*");
            Assert.That(result, Is.EqualTo("bold and italic"));
        }

        [Test]
        public void MarkdownToPlainText_Heading_StripsHashPrefix()
        {
            string result = MarkdownRenderer.MarkdownToPlainText("## Section title");
            Assert.That(result, Is.EqualTo("Section title"));
        }

        [Test]
        public void MarkdownToPlainText_WarningBlock_StripsDelimiters()
        {
            const string md = @":::warning Heads up
Body line
:::
After";
            string result = MarkdownRenderer.MarkdownToPlainText(md);
            Assert.That(
                result,
                Is.EqualTo(
                    "Heads up"
                    + System.Environment.NewLine
                    + "Body line"
                    + System.Environment.NewLine
                    + "After"));
        }

        [Test]
        public void RenderMarkdownToString_DelegatesToPlainTextExtraction()
        {
            string result = MarkdownRenderingService.RenderMarkdownToString("[x](y)");
            Assert.That(result, Is.EqualTo("x"));
        }
    }
}
