// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using JetBrains.Annotations;

using KOTORModSync.Core.Utility;

namespace KOTORModSync.Converters
{
    public static class MarkdownRenderer
    {
        private sealed class ThemeAwareRegistration
        {
            public ThemeAwareRegistration(WeakReference<AvaloniaObject> target, Action<AvaloniaObject> apply)
            {
                Target = target;
                Apply = apply;
            }

            public WeakReference<AvaloniaObject> Target { get; }

            public Action<AvaloniaObject> Apply { get; }
        }

        private static readonly object ThemeRegistrationLock = new object();
        private static readonly List<ThemeAwareRegistration> ThemeAwareElements = new List<ThemeAwareRegistration>();

        static MarkdownRenderer()
        {
            ThemeManager.StyleChanged += _ => ReapplyThemeAwareElements();
        }

        private static void RegisterThemeAwareElement(AvaloniaObject target, Action<AvaloniaObject> apply)
        {
            if (target is null || apply is null)
            {
                return;
            }

            lock (ThemeRegistrationLock)
            {
                for (int i = ThemeAwareElements.Count - 1; i >= 0; i--)
                {
                    if (!ThemeAwareElements[i].Target.TryGetTarget(out AvaloniaObject existing) || existing is null)
                    {
                        ThemeAwareElements.RemoveAt(i);
                        continue;
                    }

                    if (ReferenceEquals(existing, target))
                    {
                        ThemeAwareElements.RemoveAt(i);
                    }
                }

                ThemeAwareElements.Add(new ThemeAwareRegistration(new WeakReference<AvaloniaObject>(target), apply));
            }
        }

        private static void ReapplyThemeAwareElements()
        {
            void ApplyAll()
            {
                lock (ThemeRegistrationLock)
                {
                    for (int i = ThemeAwareElements.Count - 1; i >= 0; i--)
                    {
                        ThemeAwareRegistration registration = ThemeAwareElements[i];
                        if (!registration.Target.TryGetTarget(out AvaloniaObject target) || target is null)
                        {
                            ThemeAwareElements.RemoveAt(i);
                            continue;
                        }

                        registration.Apply(target);
                    }
                }
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyAll();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyAll, DispatcherPriority.Normal);
            }
        }

        private static void ApplyThemeBrush(
            AvaloniaObject target,
            AvaloniaProperty<IBrush> property,
            string resourceKey,
            [CanBeNull] Func<IBrush> fallbackFactory = null)
        {
            if (target is null)
            {
                return;
            }

            void Apply(AvaloniaObject obj)
            {
                if (obj is null)
                {
                    return;
                }

                IBrush brush = ThemeResourceHelper.GetBrush(resourceKey);
                if (brush is null || Equals(brush, Brushes.Transparent))
                {
                    brush = fallbackFactory?.Invoke();
                }

                if (brush is null)
                {
                    brush = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
                }

                obj.SetValue(property, brush);
            }

            Apply(target);
            RegisterThemeAwareElement(target, Apply);
        }

        private static void ApplyThemeForeground(AvaloniaObject target, AvaloniaProperty<IBrush> property)
        {
            ApplyThemeBrush(target, property, "ThemeForegroundBrush", CreateThemeForegroundBrush);
        }

        private static void ApplyLinkForeground(AvaloniaObject target, AvaloniaProperty<IBrush> property)
        {
            ApplyThemeBrush(target, property, "LinkForegroundBrush", () => new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)));
        }

        private sealed class MarkdownHyperlinkInline : InlineUIContainer
        {
            private readonly string _linkUrl;
            private readonly Action<string> _onClick;
            private readonly Border _host;
            private readonly TextBlock _textBlock;

            public MarkdownHyperlinkInline(string linkText, string linkUrl, Action<string> onClick)
            {
                _linkUrl = linkUrl ?? string.Empty;
                _onClick = onClick;

                _textBlock = new TextBlock
                {
                    Text = linkText ?? string.Empty,
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    TextDecorations = new TextDecorationCollection
                    {
                        new TextDecoration
                        {
                            Location = TextDecorationLocation.Underline,
                        },
                    },
                };
                ApplyLinkForeground(_textBlock, TextElement.ForegroundProperty);

                _host = new Border
                {
                    Child = _textBlock,
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Focusable = true,
                };

                _host.PointerReleased += OnPointerReleased;
                _host.PointerPressed += OnPointerPressed;
                _host.KeyDown += OnHostKeyDown;

                Child = _host;

                PropertyChanged += OnInlinePropertyChanged;

                SyncTextStyling();
            }

            private void OnInlinePropertyChanged(object sender, AvaloniaPropertyChangedEventArgs change)
            {
                if (change.Property == FontSizeProperty ||
                    change.Property == FontFamilyProperty ||
                    change.Property == FontStyleProperty ||
                    change.Property == FontWeightProperty ||
                    change.Property == FontStretchProperty)
                {
                    SyncTextStyling();
                }
            }

            private void SyncTextStyling()
            {
                if (!double.IsNaN(FontSize) && FontSize > 0)
                {
                    _textBlock.FontSize = FontSize;
                }

                if (FontFamily != null)
                {
                    _textBlock.FontFamily = FontFamily;
                }

                _textBlock.FontStyle = FontStyle;
                _textBlock.FontWeight = FontWeight;
                _textBlock.FontStretch = FontStretch;
            }

            private void OnPointerPressed(object sender, PointerPressedEventArgs e)
            {
                if (e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                }
            }

            private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
            {
                if (!e.Handled && e.InitialPressMouseButton == MouseButton.Left)
                {
                    ActivateLink();
                    e.Handled = true;
                }
            }

            private void OnHostKeyDown(object sender, KeyEventArgs e)
            {
                if (!e.Handled && (e.Key == Key.Enter || e.Key == Key.Space))
                {
                    ActivateLink();
                    e.Handled = true;
                }
            }

            private void ActivateLink()
            {
                _onClick?.Invoke(_linkUrl);
            }
        }

        private static IBrush CreateThemeForegroundBrush()
        {
            string currentTheme = ThemeManager.GetCurrentStylePath();
            if (!string.IsNullOrEmpty(currentTheme))
            {
                if (NetFrameworkCompatibility.Contains(currentTheme, "Kotor2Style", StringComparison.OrdinalIgnoreCase))
                {
                    return new SolidColorBrush(Color.FromRgb(0x18, 0xAE, 0x88)); // #18AE88
                }

                if (NetFrameworkCompatibility.Contains(currentTheme, "KotorStyle", StringComparison.OrdinalIgnoreCase))
                {
                    return new SolidColorBrush(Color.FromRgb(0x3A, 0xAA, 0xFF)); // #3AAAFF
                }
            }

            return new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)); // Light theme default
        }

        /// <summary>
        /// Converts markdown to plain text: strips link syntax (keeping link text), bold/italic markers,
        /// heading prefixes, and warning block delimiters. Line breaks are normalized to <see cref="Environment.NewLine"/>.
        /// </summary>
        [NotNull]
        public static string MarkdownToPlainText([CanBeNull] string markdownText)
        {
            if (string.IsNullOrWhiteSpace(markdownText))
            {
                return string.Empty;
            }

            try
            {
                string[] lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var output = new StringBuilder();
                bool inWarningBlock = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string rawLine = lines[i];
                    string lineTrimEnd = rawLine.TrimEnd();
                    string lineTrim = lineTrimEnd.Trim();

                    if (inWarningBlock)
                    {
                        if (string.Equals(lineTrim, ":::", StringComparison.Ordinal))
                        {
                            inWarningBlock = false;
                            continue;
                        }

                        AppendPlainLine(output, StripInlineMarkdown(lineTrimEnd));
                        continue;
                    }

                    if (lineTrim.StartsWith(":::warning", StringComparison.OrdinalIgnoreCase))
                    {
                        inWarningBlock = true;
                        string titlePart = lineTrim.Length > ":::warning".Length
                            ? lineTrim.Substring(":::warning".Length).Trim()
                            : string.Empty;
                        if (!string.IsNullOrEmpty(titlePart))
                        {
                            AppendPlainLine(output, StripInlineMarkdown(titlePart));
                        }

                        continue;
                    }

                    if (lineTrim.StartsWith("#", StringComparison.Ordinal))
                    {
                        int level = 0;
                        while (level < lineTrim.Length && lineTrim[level] == '#')
                        {
                            level++;
                        }

                        string headingBody = lineTrim.Substring(level).Trim();
                        if (!string.IsNullOrEmpty(headingBody))
                        {
                            AppendPlainLine(output, StripInlineMarkdown(headingBody));
                        }

                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(lineTrimEnd))
                    {
                        continue;
                    }

                    AppendPlainLine(output, StripInlineMarkdown(lineTrimEnd));
                }

                return output.ToString().TrimEnd();
            }
            catch (Exception)
            {
                return markdownText;
            }
        }

        private static void AppendPlainLine(StringBuilder output, string line)
        {
            if (output.Length > 0)
            {
                output.Append(Environment.NewLine);
            }

            output.Append(line);
        }

        /// <summary>
        /// Strips <c>[text](url)</c>, <c>**bold**</c>, <c>__bold__</c>, and single-<c>*</c>/<c>_</c> italic spans
        /// (same subset as <see cref="ParseMarkdownInlines"/>).
        /// </summary>
        private static string StripInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string withLinks = Regex.Replace(
                text,
                @"\[([^\]]+)\]\(([^)]+)\)",
                m => m.Groups[1].Value,
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(5));

            string noBold = Regex.Replace(
                withLinks,
                @"(\*\*|__)([^*_]+)\1",
                m => m.Groups[2].Value,
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(5));

            return Regex.Replace(
                noBold,
                @"(\*|_)([^*_]+)\1",
                m => m.Groups[2].Value,
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(5));
        }

        [NotNull]
        public static TextBlock RenderToTextBlock(
            [CanBeNull] string markdownText,
            [CanBeNull] Action<string> onLinkClick = null)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWithOverflow,
                TextTrimming = TextTrimming.None,
            };
            ApplyThemeForeground(textBlock, TextBlock.ForegroundProperty);

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                return textBlock;
            }

            try
            {
                if (textBlock.Inlines != null)
                {
                    textBlock.Inlines.AddRange(ParseMarkdownInlines(markdownText, onLinkClick));
                }
            }
            catch (Exception)
            {

                textBlock.Text = markdownText;
            }

            return textBlock;
        }

        /// <summary>
        /// Renders markdown content to a Panel, supporting block-level elements like headings and warning blocks.
        /// </summary>
        [NotNull]
        public static Panel RenderToPanel(
            [CanBeNull] string markdownText,
            [CanBeNull] Action<string> onLinkClick = null)
        {
            var mainPanel = new StackPanel
            {
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                return mainPanel;
            }

            try
            {
                string[] lines = markdownText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                int i = 0;

                while (i < lines.Length)
                {
                    string line = lines[i].TrimEnd();

                    // Handle warning blocks (:::warning ... :::)
                    if (line.StartsWith(":::warning", StringComparison.OrdinalIgnoreCase))
                    {
                        Border warningBlock = ParseWarningBlock(lines, ref i, onLinkClick);
                        if (warningBlock != null)
                        {
                            mainPanel.Children.Add(warningBlock);
                        }
                        continue;
                    }

                    // Handle headings (# ## ###)
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        int headingLevel = 0;
                        while (headingLevel < line.Length && line[headingLevel] == '#')
                        {
                            headingLevel++;
                        }

                        string headingText = line.Substring(headingLevel).Trim();
                        if (!string.IsNullOrEmpty(headingText))
                        {
                            var headingBlock = new TextBlock
                            {
                                TextWrapping = TextWrapping.Wrap,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                            };
                            ApplyThemeForeground(headingBlock, TextBlock.ForegroundProperty);

                            // Set font size based on heading level
                            switch (headingLevel)
                            {
                                case 1:
                                    headingBlock.FontSize = 28;
                                    break;
                                case 2:
                                    headingBlock.FontSize = 24;
                                    break;
                                case 3:
                                    headingBlock.FontSize = 20;
                                    break;
                                case 4:
                                    headingBlock.FontSize = 18;
                                    break;
                                default:
                                    headingBlock.FontSize = 16;
                                    break;
                            }
                            headingBlock.FontWeight = FontWeight.Bold;
                            headingBlock.Margin = new Thickness(0, headingLevel == 1 ? 0 : 8, 0, 4);

                            if (headingBlock.Inlines != null)
                            {
                                headingBlock.Inlines.AddRange(ParseMarkdownInlines(headingText, onLinkClick));
                            }

                            mainPanel.Children.Add(headingBlock);
                        }
                        i++;
                        continue;
                    }

                    // Handle regular paragraphs (collect consecutive non-empty lines)
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var paragraphBuilder = new System.Text.StringBuilder();
                        paragraphBuilder.Append(line);
                        i++;

                        // Collect consecutive non-empty, non-heading, non-warning lines into a paragraph
                        while (i < lines.Length)
                        {
                            string nextLine = lines[i].TrimEnd();

                            // Stop if we hit a heading, warning block, or double newline
                            if (nextLine.StartsWith("#", StringComparison.Ordinal) ||
                                nextLine.StartsWith(":::", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }

                            // If we hit an empty line, check if it's a paragraph break
                            if (string.IsNullOrWhiteSpace(nextLine))
                            {
                                i++;
                                // If next non-empty line is also a paragraph, this is a paragraph break
                                int nextNonEmpty = i;
                                while (nextNonEmpty < lines.Length && string.IsNullOrWhiteSpace(lines[nextNonEmpty]))
                                {
                                    nextNonEmpty++;
                                }
                                if (nextNonEmpty < lines.Length &&
                                    !lines[nextNonEmpty].TrimEnd().StartsWith("#", StringComparison.Ordinal) &&
                                    !lines[nextNonEmpty].TrimEnd().StartsWith(":::", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Continue paragraph after empty line
                                    paragraphBuilder.Append(' ');
                                    i = nextNonEmpty;
                                    continue;
                                }
                                break;
                            }

                            paragraphBuilder.Append(' ');
                            paragraphBuilder.Append(nextLine);
                            i++;
                        }

                        string paragraphText = paragraphBuilder.ToString();
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            var paragraphBlock = new TextBlock
                            {
                                TextWrapping = TextWrapping.Wrap,
                                FontSize = 15,
                                LineHeight = 24,
                                Margin = new Thickness(0, 0, 0, 8),
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                            };
                            ApplyThemeForeground(paragraphBlock, TextBlock.ForegroundProperty);

                            paragraphBlock.Inlines?.AddRange(ParseMarkdownInlines(paragraphText, onLinkClick));

                            mainPanel.Children.Add(paragraphBlock);
                        }
                        continue;
                    }

                    i++;
                }
            }
            catch (Exception)
            {
                // Fallback to simple text block
                var fallbackBlock = new TextBlock
                {
                    Text = markdownText,
                    TextWrapping = TextWrapping.Wrap,
                };
                ApplyThemeForeground(fallbackBlock, TextBlock.ForegroundProperty);
                mainPanel.Children.Add(fallbackBlock);
            }

            return mainPanel;
        }

        private static Border ParseWarningBlock(
            string[] lines,
            ref int currentIndex,
            Action<string> onLinkClick)
        {
            var warningPanel = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            };

            var iconBlock = new TextBlock
            {
                Text = "⚠️",
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 12, 0),
            };

            var textPanel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            // Parse title - check if it's on the same line as :::warning or next line
            string line = currentIndex < lines.Length ? lines[currentIndex] : string.Empty;
            string titleLine = string.Empty;

            if (line.StartsWith(":::warning", StringComparison.OrdinalIgnoreCase))
            {
                titleLine = line.Substring(":::warning".Length).Trim();
                currentIndex++;
            }

            if (!string.IsNullOrWhiteSpace(titleLine))
            {
                var titleBlock = new TextBlock
                {
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                ApplyThemeForeground(titleBlock, TextBlock.ForegroundProperty);

                if (titleBlock.Inlines != null)
                {
                    titleBlock.Inlines.AddRange(ParseMarkdownInlines(titleLine, onLinkClick));
                }

                textPanel.Children.Add(titleBlock);
            }

            // Parse content until closing :::
            var contentBuilder = new System.Text.StringBuilder();
            while (currentIndex < lines.Length)
            {
                string contentLine = lines[currentIndex];
                if (string.Equals(contentLine.Trim(), ":::", StringComparison.Ordinal))
                {
                    currentIndex++;
                    break;
                }

                if (contentBuilder.Length > 0)
                {
                    contentBuilder.AppendLine();
                }
                contentBuilder.Append(contentLine);
                currentIndex++;
            }

            string contentText = contentBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(contentText))
            {
                var contentBlock = new TextBlock
                {
                    FontSize = 11,
                    Opacity = 0.9,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                ApplyThemeForeground(contentBlock, TextBlock.ForegroundProperty);

                contentBlock.Inlines?.AddRange(ParseMarkdownInlines(contentText, onLinkClick));

                textPanel.Children.Add(contentBlock);
            }

            Grid.SetColumn(iconBlock, 0);
            Grid.SetColumn(textPanel, 1);
            warningPanel.Children.Add(iconBlock);
            warningPanel.Children.Add(textPanel);

            var warningBorder = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Child = warningPanel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            // Try to get warning background color from theme, fallback to yellow
            try
            {
                if (Application.Current?.Resources.TryGetResource("WarningBackgroundBrush", theme: null, out object resource) == true
                    && resource is IBrush warningBrush)
                {
                    warningBorder.Background = warningBrush;
                }
                else
                {
                    // Default yellow background
                    warningBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B));
                }
            }
            catch
            {
                warningBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B));
            }

            return warningBorder;
        }

        private static List<Inline> ParseMarkdownInlines(
            [NotNull] string text,
            [CanBeNull] Action<string> onLinkClick = null)
        {
            var inlines = new List<Inline>();
            int currentIndex = 0;


            // Use Regex.CompileToAssembly or timeout-parameter overload to avoid ReDoS
            MatchCollection linkMatches = new Regex(
                @"\[([^\]]+)\]\(([^)]+)\)",
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(5)
            ).Matches(text);

            foreach (Match match in linkMatches)
            {
                if (match.Index > currentIndex)
                {
                    string beforeText = text.Substring(currentIndex, match.Index - currentIndex);
                    AddTextWithFormatting(beforeText, inlines);
                }

                string linkText = match.Groups[1].Value;
                string linkUrl = match.Groups[2].Value;

                if (onLinkClick != null)
                {
                    var hyperlink = new MarkdownHyperlinkInline(linkText, linkUrl, onLinkClick);
                    inlines.Add(hyperlink);
                }
                else
                {
                    var linkRun = new Run
                    {
                        Text = linkText,
                        TextDecorations = TextDecorations.Underline,
                    };
                    ApplyLinkForeground(linkRun, TextElement.ForegroundProperty);
                    inlines.Add(linkRun);
                }

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                string remainingText = text.Substring(currentIndex);
                AddTextWithFormatting(remainingText, inlines);
            }

            return inlines;
        }

        private static void AddTextWithFormatting(
            [NotNull] string text,
            [NotNull] List<Inline> inlines)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            MatchCollection boldMatches = new Regex(
                @"(\*\*|__)([^*_]+)\1",
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(5)
            ).Matches(text);
            int currentIndex = 0;

            foreach (Match match in boldMatches)
            {

                if (match.Index > currentIndex)
                {
                    string beforeText = text.Substring(currentIndex, match.Index - currentIndex);
                    AddTextWithItalic(beforeText, inlines);
                }

                string boldText = match.Groups[2].Value;
                var boldRun = new Run
                {
                    Text = boldText,
                    FontWeight = FontWeight.Bold,
                };
                ApplyThemeForeground(boldRun, TextElement.ForegroundProperty);
                inlines.Add(boldRun);

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                string remainingText = text.Substring(currentIndex);
                AddTextWithItalic(remainingText, inlines);
            }
        }

        private static void AddTextWithItalic(
            [NotNull] string text,
            [NotNull] List<Inline> inlines)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string italicPattern = @"(\*|_)([^*_]+)\1";
            MatchCollection italicMatches = new Regex(
                italicPattern,
                RegexOptions.Compiled,
                TimeSpan.FromSeconds(5)
            ).Matches(text);
            int currentIndex = 0;

            foreach (Match match in italicMatches)
            {

                if (match.Index > currentIndex)
                {
                    string beforeText = text.Substring(currentIndex, match.Index - currentIndex);
                    var beforeRun = new Run { Text = beforeText };
                    ApplyThemeForeground(beforeRun, TextElement.ForegroundProperty);
                    inlines.Add(beforeRun);
                }

                string italicText = match.Groups[2].Value;
                var italicRun = new Run
                {
                    Text = italicText,
                    FontStyle = FontStyle.Italic,
                };
                ApplyThemeForeground(italicRun, TextElement.ForegroundProperty);
                inlines.Add(italicRun);

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                string remainingText = text.Substring(currentIndex);
                var remainingRun = new Run { Text = remainingText };
                ApplyThemeForeground(remainingRun, TextElement.ForegroundProperty);
                inlines.Add(remainingRun);
            }
        }
    }
}
