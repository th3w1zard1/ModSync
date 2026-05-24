// Copyright 2021-2025 KOTORModSync
// Licensed under the Business Source License 1.1 (BSL 1.1).
// See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;

using KOTORModSync.Converters;
using KOTORModSync.Core;
using KOTORModSync.Core.Utility;

namespace KOTORModSync.Services
{
    public class MarkdownRenderingService
    {
        /// <summary>
        /// Renders markdown content to a TextBlock's Inlines collection.
        /// </summary>
        public void RenderMarkdownToTextBlock(TextBlock targetTextBlock, string markdownContent)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RenderMarkdownToTextBlock(targetTextBlock, markdownContent), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (targetTextBlock is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(markdownContent))
                {
                    targetTextBlock.Inlines?.Clear();
                    return;
                }

                TextBlock renderedContent = MarkdownRenderer.RenderToTextBlock(
                    markdownContent,
                    OpenUrl
                );

                targetTextBlock.Inlines?.Clear();
                targetTextBlock.Inlines?.AddRange(
                    renderedContent.Inlines
                    ?? throw new InvalidOperationException("renderedContent.Inlines is null: " + markdownContent)
                );
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error rendering markdown content");
            }
        }

        /// <summary>
        /// Renders markdown content and returns the Inlines collection.
        /// Useful for converters that need to return rendered content.
        /// </summary>
        public static List<Inline> RenderMarkdownToInlines(string markdownContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdownContent))
                {
                    return new List<Inline>();
                }

                TextBlock renderedContent = MarkdownRenderer.RenderToTextBlock(markdownContent, onLinkClick: null);
                return renderedContent.Inlines?.Count > 0
                    ? new List<Inline>(renderedContent.Inlines)
                    : new List<Inline>();
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error rendering markdown to inlines");
                return new List<Inline> { new Run { Text = markdownContent } };
            }
        }

        /// <summary>
        /// Renders markdown content and returns a plain string (for converters that need string output).
        /// This strips formatting but preserves the text content.
        /// </summary>
        public static string RenderMarkdownToString(string markdownContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdownContent))
                {
                    return string.Empty;
                }

                return MarkdownRenderer.MarkdownToPlainText(markdownContent);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error rendering markdown to string");
                return markdownContent ?? string.Empty;
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility. Use RenderMarkdownToTextBlock instead.
        /// </summary>
        public void RenderComponentMarkdown(
            ModComponent component,
            TextBlock descriptionTextBlock,
            TextBlock directionsTextBlock,
            bool spoilerFreeMode = false)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RenderComponentMarkdown(component, descriptionTextBlock, directionsTextBlock, spoilerFreeMode), DispatcherPriority.Normal);
                return;
            }
            try
            {
                if (component is null)
                {
                    return;
                }

                if (descriptionTextBlock != null)
                {
                    string descriptionContent;
                    if (spoilerFreeMode)
                    {
                        // Only use custom spoiler-free description if it's provided
                        if (!string.IsNullOrWhiteSpace(component.DescriptionSpoilerFree))
                        {
                            descriptionContent = component.DescriptionSpoilerFree;
                        }
                        else
                        {
                            // Fall back to auto-generated spoiler-free description
                            descriptionContent = Converters.SpoilerFreeContentConverter.GenerateSpoilerFreeDescription(component);
                        }
                    }
                    else
                    {
                        descriptionContent = component.Description;
                    }

                    RenderMarkdownToTextBlock(descriptionTextBlock, descriptionContent);
                }

                if (directionsTextBlock != null)
                {
                    string directionsContent;
                    if (spoilerFreeMode)
                    {
                        // Only use custom spoiler-free directions if provided
                        if (!string.IsNullOrWhiteSpace(component.DirectionsSpoilerFree))
                        {
                            directionsContent = component.DirectionsSpoilerFree;
                        }
                        else
                        {
                            // Fall back to generic message (directions don't have auto-generation)
                            directionsContent = "Installation instructions available. Please review carefully before proceeding.";
                        }
                    }
                    else
                    {
                        directionsContent = component.Directions;
                    }

                    RenderMarkdownToTextBlock(directionsTextBlock, directionsContent);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error rendering component markdown");
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                UrlUtilities.OpenUrl(url);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Error opening URL: {url}");
            }
        }

    }
}
