using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using AvRichTextBox;
using HoloPatcher.UI.Rte;
using HoloPatcher.UI.ViewModels;
using MsBox.Avalonia;

namespace HoloPatcher.UI.Views
{

    public partial class MainWindow : Window
    {
        private ScrollViewer _logScrollViewer;
        private RichTextBox _rtfRichTextBox;
        private Avalonia.Controls.TextBlock _logTextBlock;

        public MainWindow()
        {
            InitializeComponent();

            // Get reference to scroll viewer, RichTextBox, and LogTextBlock
            _logScrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
            _rtfRichTextBox = this.FindControl<RichTextBox>("RtfRichTextBox");
            _logTextBlock = this.FindControl<Avalonia.Controls.TextBlock>("LogTextBlock");

            // Subscribe to data context changes to set up auto-scroll and log formatting
            DataContextChanged += OnDataContextChanged;

            // Set up window centering on startup (matches Python's set_window)
            Opened += OnWindowOpened;
        }

        private async void OnWindowOpened(object sender, EventArgs e)
        {
            // Center window on screen - matches Python's set_window behavior
            if (Screens.Primary != null)
            {
                Avalonia.PixelRect screen = Screens.Primary.WorkingArea;
                int x = (int)((screen.Width - Width) / 2);
                int y = (int)((screen.Height - Height) / 2);
                Position = new Avalonia.PixelPoint(x, y);
            }

            // Show alpha/demo warning on startup
            await ShowAlphaWarning();
        }

        private async System.Threading.Tasks.Task ShowAlphaWarning()
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                // Only show warning if version is alpha
                if (!viewModel.IsAlphaVersion)
                {
                    return;
                }

                MsBox.Avalonia.Base.IMsBox<MsBox.Avalonia.Enums.ButtonResult> messageBox = MsBox.Avalonia.MessageBoxManager.GetMessageBoxStandard(
                    "ALPHA VERSION WARNING",
                    $"⚠️ WARNING: This is an ALPHA version ({Core.VersionLabel}) of KPatcher\n\n" +
                    "This version is for testing and demonstration purposes only.\n" +
                    "It is NOT intended for production use.\n\n" +
                    "Features may be incomplete, unstable, or contain bugs.\n" +
                    "Use at your own risk.\n\n" +
                    "For production use, please use the stable release.",
                    MsBox.Avalonia.Enums.ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Warning);
                await messageBox.ShowAsync();
            }
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            Console.WriteLine("[RTF] OnDataContextChanged called");
            if (DataContext is MainWindowViewModel viewModel)
            {
                Console.WriteLine("[RTF] Subscribing to PropertyChanged events");
                // Subscribe to property changes to auto-scroll log and load RTF
                viewModel.PropertyChanged += ViewModel_PropertyChanged;

                // Also check if RTF content is already set
                if (viewModel.IsRtfContent && !string.IsNullOrEmpty(viewModel.RtfContent))
                {
                    Console.WriteLine("[RTF] RTF content already set, loading immediately");
                    Dispatcher.UIThread.Post(() =>
                    {
                        LoadRtfContent();
                    }, DispatcherPriority.Normal);
                }
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Console.WriteLine($"[RTF] PropertyChanged: {e.PropertyName}");
            if (e.PropertyName == nameof(MainWindowViewModel.LogText))
            {
                // Format and display log text with colors and proper font sizes - matches Python's set_text_font
                Dispatcher.UIThread.Post(() =>
                {
                    FormatLogText();
                    _logScrollViewer?.ScrollToEnd();
                }, DispatcherPriority.Background);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.RtfContent))
            {
                Console.WriteLine("[RTF] RtfContent property changed, loading RTF");
                // Load RTF content into RichTextBox when it changes - use Normal priority to ensure it happens
                Dispatcher.UIThread.Post(() =>
                {
                    LoadRtfContent();
                }, DispatcherPriority.Normal);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsRtfContent))
            {
                Console.WriteLine($"[RTF] IsRtfContent changed to: {((MainWindowViewModel)sender).IsRtfContent}");
                // If RTF content is enabled and we have content, load it
                if (DataContext is MainWindowViewModel vm && vm.IsRtfContent && !string.IsNullOrEmpty(vm.RtfContent))
                {
                    Console.WriteLine("[RTF] IsRtfContent=true and RtfContent exists, loading RTF");
                    Dispatcher.UIThread.Post(() =>
                    {
                        LoadRtfContent();
                    }, DispatcherPriority.Normal);
                }
            }
        }

        private void LoadRtfContent()
        {
            Console.WriteLine("[RTF] LoadRtfContent() called");

            // Try to get RichTextBox if not already found
            if (_rtfRichTextBox is null)
            {
                Console.WriteLine("[RTF] RichTextBox not cached, searching for control...");
                _rtfRichTextBox = this.FindControl<RichTextBox>("RtfRichTextBox");
                if (_rtfRichTextBox is null)
                {
                    Console.WriteLine("[RTF] ERROR: RtfRichTextBox control not found by name 'RtfRichTextBox'!");
                    Console.WriteLine("[RTF] This might mean the XAML control name doesn't match or the control isn't loaded yet.");
                    return;
                }
                else
                {
                    Console.WriteLine("[RTF] Found RichTextBox by name");
                }
            }

            if (_rtfRichTextBox is null)
            {
                Console.WriteLine("[RTF] ERROR: RtfRichTextBox is still null!");
                return;
            }

            Console.WriteLine($"[RTF] RichTextBox found: IsVisible={_rtfRichTextBox.IsVisible}, IsEnabled={_rtfRichTextBox.IsEnabled}");
            Console.WriteLine($"[RTF] RichTextBox type: {_rtfRichTextBox.GetType().FullName}");
            Console.WriteLine($"[RTF] RichTextBox IsLoaded: {_rtfRichTextBox.IsLoaded}");

            if (!(DataContext is MainWindowViewModel viewModel))
            {
                Console.WriteLine("[RTF] ERROR: DataContext is not MainWindowViewModel!");
                return;
            }

            if (viewModel.ActiveRteDocument != null)
            {
                Console.WriteLine("[RTF] Rendering RTE document via FlowDocument");
                RteDocumentConverter.ApplyToRichTextBox(_rtfRichTextBox, viewModel.ActiveRteDocument);
                return;
            }

            // Ensure RichTextBox is visible before loading
            if (!_rtfRichTextBox.IsVisible)
            {
                Console.WriteLine("[RTF] WARNING: RichTextBox is not visible, making it visible...");
                _rtfRichTextBox.IsVisible = true;
            }

            // Wait for control to be fully loaded and initialized
            // The RichTextBox needs to be fully loaded before we can access FlowDocument
            if (!_rtfRichTextBox.IsLoaded)
            {
                Console.WriteLine("[RTF] WARNING: RichTextBox is not loaded yet, subscribing to Loaded event...");
                // Subscribe to Loaded event and wait for it
                EventHandler<Avalonia.Interactivity.RoutedEventArgs> loadedHandler = null;
                loadedHandler = (s, e) =>
                {
                    _rtfRichTextBox.Loaded -= loadedHandler;
                    Console.WriteLine("[RTF] RichTextBox Loaded event fired, waiting for initialization...");
                    // Wait a bit more to ensure FlowDocument is fully initialized
                    // Post to dispatcher with a delay priority to ensure all initialization is complete
                    Dispatcher.UIThread.Post(() =>
                    {
                        // Post again with lower priority to ensure initialization completes
                        Dispatcher.UIThread.Post(() =>
                        {
                            LoadRtfContent();
                        }, DispatcherPriority.Background);
                    }, DispatcherPriority.Loaded);
                };
                _rtfRichTextBox.Loaded += loadedHandler;
                return;
            }

            // Additional check: ensure FlowDocument is initialized
            try
            {
                PropertyInfo flowDocProperty = _rtfRichTextBox.GetType().GetProperty("FlowDoc");
                if (flowDocProperty != null)
                {
                    object flowDoc = flowDocProperty.GetValue(_rtfRichTextBox);
                    if (flowDoc is null)
                    {
                        Console.WriteLine("[RTF] WARNING: FlowDoc is null, waiting for initialization...");
                        Dispatcher.UIThread.Post(() => LoadRtfContent(), DispatcherPriority.Loaded);
                        return;
                    }
                    Console.WriteLine("[RTF] FlowDoc is initialized");
                }
            }
            catch (Exception initEx)
            {
                Console.WriteLine($"[RTF] Warning: Error checking FlowDoc initialization: {initEx.Message}");
                // Continue anyway
            }

            string rtfContent = viewModel.RtfContent;
            if (string.IsNullOrEmpty(rtfContent))
            {
                Console.WriteLine("[RTF] WARNING: RtfContent is empty");
                return;
            }

            Console.WriteLine($"[RTF] Loading RTF content, length: {rtfContent.Length}");
            Console.WriteLine($"[RTF] RTF preview (first 200 chars): {rtfContent.Substring(0, Math.Min(200, rtfContent.Length))}");

            try
            {
                // Method 1: Try loading RTF from a MemoryStream
                Console.WriteLine("[RTF] Attempting Method 1: Load RTF from MemoryStream");
                try
                {
                    using (var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rtfContent)))
                    {
                        // Try to find a LoadRtf method that accepts Stream
                        MethodInfo loadRtfStreamMethod = _rtfRichTextBox.GetType().GetMethod("LoadRtf", new[] { typeof(Stream) });
                        if (loadRtfStreamMethod != null)
                        {
                            Console.WriteLine("[RTF] Found LoadRtf(Stream) method, attempting to use it");
                            loadRtfStreamMethod.Invoke(_rtfRichTextBox, new object[] { memoryStream });
                            Console.WriteLine("[RTF] RTF loaded successfully using LoadRtf(Stream)");
                            return;
                        }

                        // Try alternative: Load from TextReader
                        MethodInfo loadRtfTextReaderMethod = _rtfRichTextBox.GetType().GetMethod("LoadRtf", new[] { typeof(TextReader) });
                        if (loadRtfTextReaderMethod != null)
                        {
                            Console.WriteLine("[RTF] Found LoadRtf(TextReader) method, attempting to use it");
                            memoryStream.Position = 0; // Reset stream position
                            using (var textReader = new StreamReader(memoryStream, System.Text.Encoding.UTF8, true, 1024, true))
                            {
                                loadRtfTextReaderMethod.Invoke(_rtfRichTextBox, new object[] { textReader });
                                Console.WriteLine("[RTF] RTF loaded successfully using LoadRtf(TextReader)");
                                return;
                            }
                        }
                    }
                }
                catch (Exception streamEx)
                {
                    Console.WriteLine($"[RTF] Method 1 failed: {streamEx.GetType().Name}: {streamEx.Message}");
                    if (streamEx.InnerException != null)
                    {
                        Console.WriteLine($"[RTF] Method 1 inner exception: {streamEx.InnerException.GetType().Name}: {streamEx.InnerException.Message}");
                    }
                }

                // Method 2: Load RTF directly from string using RichTextBox.LoadRtf(string)
                // This is the preferred method - loads RTF content directly without temp file
                Console.WriteLine("[RTF] Attempting Method 2: Load RTF directly from string");
                try
                {
                    // Ensure FlowDocument is initialized first
                    PropertyInfo flowDocProperty = _rtfRichTextBox.GetType().GetProperty("FlowDoc");
                    if (flowDocProperty != null)
                    {
                        object flowDoc = flowDocProperty.GetValue(_rtfRichTextBox);
                        if (flowDoc is null)
                        {
                            Console.WriteLine("[RTF] WARNING: FlowDoc is null, cannot load RTF");
                            throw new InvalidOperationException("FlowDocument is not initialized");
                        }
                        Console.WriteLine("[RTF] FlowDoc is available, proceeding with LoadRtf");

                        // Ensure the document has been properly initialized by checking if Selection exists
                        PropertyInfo selectionProperty = flowDoc.GetType().GetProperty("Selection");
                        if (selectionProperty != null)
                        {
                            object selection = selectionProperty.GetValue(flowDoc);
                            if (selection is null)
                            {
                                Console.WriteLine("[RTF] WARNING: Selection is null, waiting a bit longer...");
                                // Wait a bit more for initialization
                                Dispatcher.UIThread.Post(() => LoadRtfContent(), DispatcherPriority.Background);
                                return;
                            }
                        }
                    }

                    // Use LoadRtf(string) which takes RTF content directly
                    // The LoadRtf method will call InitializeDocument() which may throw if Selection is not initialized
                    // We need to ensure the FlowDocument has a valid Selection object before loading
                    PropertyInfo flowDocProperty2 = _rtfRichTextBox.GetType().GetProperty("FlowDoc");
                    if (flowDocProperty2 != null)
                    {
                        object flowDoc = flowDocProperty2.GetValue(_rtfRichTextBox);
                        if (flowDoc != null)
                        {
                            // Ensure Selection is initialized - check if it exists and is not null
                            PropertyInfo selectionProperty = flowDoc.GetType().GetProperty("Selection");
                            if (selectionProperty != null)
                            {
                                object selection = selectionProperty.GetValue(flowDoc);
                                if (selection is null)
                                {
                                    Console.WriteLine("[RTF] WARNING: Selection is null, attempting to create new document first...");
                                    // Try to call NewDocument() to initialize the document properly
                                    MethodInfo newDocMethod = flowDoc.GetType().GetMethod("NewDocument");
                                    if (newDocMethod != null)
                                    {
                                        newDocMethod.Invoke(flowDoc, null);
                                        Console.WriteLine("[RTF] NewDocument() called to initialize document");
                                    }
                                }
                            }
                        }
                    }

                    // Set minimal padding on FlowDocument for better fit in narrow window
                    PropertyInfo flowDocPropertyForPadding = _rtfRichTextBox.GetType().GetProperty("FlowDoc");
                    if (flowDocPropertyForPadding != null)
                    {
                        object flowDoc = flowDocPropertyForPadding.GetValue(_rtfRichTextBox);
                        if (flowDoc != null)
                        {
                            // Set minimal padding (left, top, right, bottom) - default is 0 but RTF might set it
                            PropertyInfo pagePaddingProperty = flowDoc.GetType().GetProperty("PagePadding");
                            if (pagePaddingProperty != null)
                            {
                                // Use minimal padding: 2 pixels on all sides for better fit
                                var minimalPadding = new Avalonia.Thickness(2);
                                pagePaddingProperty.SetValue(flowDoc, minimalPadding);
                                Console.WriteLine("[RTF] Set minimal PagePadding for better fit");
                            }
                        }
                    }

                    // Now try loading RTF - this should work if Selection is initialized
                    _rtfRichTextBox.LoadRtf(rtfContent);
                    Console.WriteLine("[RTF] RTF loaded successfully using LoadRtf(string)");

                    // After loading, ensure padding is still minimal (RTF might override it)
                    if (flowDocPropertyForPadding != null)
                    {
                        object flowDoc = flowDocPropertyForPadding.GetValue(_rtfRichTextBox);
                        if (flowDoc != null)
                        {
                            PropertyInfo pagePaddingProperty = flowDoc.GetType().GetProperty("PagePadding");
                            if (pagePaddingProperty != null)
                            {
                                var minimalPadding = new Avalonia.Thickness(2);
                                pagePaddingProperty.SetValue(flowDoc, minimalPadding);
                            }
                        }
                    }
                    return;
                }
                catch (NullReferenceException nullEx)
                {
                    Console.WriteLine($"[RTF] Method 2 failed with NullReferenceException: {nullEx.Message}");
                    Console.WriteLine($"[RTF] Stack trace: {nullEx.StackTrace}");
                    // This is likely an initialization issue - don't retry, fall back to stripped text
                    throw;
                }
                catch (Exception method2Ex)
                {
                    Console.WriteLine($"[RTF] Method 2 failed: {method2Ex.GetType().Name}: {method2Ex.Message}");
                    if (method2Ex.InnerException != null)
                    {
                        Console.WriteLine($"[RTF] Method 2 inner exception: {method2Ex.InnerException.GetType().Name}: {method2Ex.InnerException.Message}");
                    }
                }

                // Method 3: Write RTF content to a temp file and load from file path
                // Simplecto.Avalonia.RichTextBox also has LoadRtfDoc(string fileName) method
                Console.WriteLine("[RTF] Attempting Method 3: Load RTF from temporary file");
                string tempFile = Path.Combine(Path.GetTempPath(), $"holopatcher_info_{Guid.NewGuid()}.rtf");
                Console.WriteLine($"[RTF] Writing temp file: {tempFile}");
                File.WriteAllText(tempFile, rtfContent, System.Text.Encoding.UTF8);
                Console.WriteLine($"[RTF] Temp file written, size: {new FileInfo(tempFile).Length} bytes");

                // Set minimal padding on FlowDocument before loading
                PropertyInfo flowDocPropertyForPadding3 = _rtfRichTextBox.GetType().GetProperty("FlowDoc");
                if (flowDocPropertyForPadding3 != null)
                {
                    object flowDoc = flowDocPropertyForPadding3.GetValue(_rtfRichTextBox);
                    if (flowDoc != null)
                    {
                        PropertyInfo pagePaddingProperty = flowDoc.GetType().GetProperty("PagePadding");
                        if (pagePaddingProperty != null)
                        {
                            var minimalPadding = new Avalonia.Thickness(2);
                            pagePaddingProperty.SetValue(flowDoc, minimalPadding);
                        }
                    }
                }

                // Use LoadRtfDoc method which takes a file path
                _rtfRichTextBox.LoadRtfDoc(tempFile);
                Console.WriteLine("[RTF] LoadRtfDoc completed successfully");

                // Ensure padding stays minimal after loading
                if (flowDocPropertyForPadding3 != null)
                {
                    object flowDoc = flowDocPropertyForPadding3.GetValue(_rtfRichTextBox);
                    if (flowDoc != null)
                    {
                        PropertyInfo pagePaddingProperty = flowDoc.GetType().GetProperty("PagePadding");
                        if (pagePaddingProperty != null)
                        {
                            var minimalPadding = new Avalonia.Thickness(2);
                            pagePaddingProperty.SetValue(flowDoc, minimalPadding);
                        }
                    }
                }

                // Verify content was loaded
                PropertyInfo flowDocPropertyCheck = _rtfRichTextBox.GetType().GetProperty("FlowDoc");
                if (flowDocPropertyCheck != null)
                {
                    object flowDoc = flowDocPropertyCheck.GetValue(_rtfRichTextBox);
                    Console.WriteLine($"[RTF] FlowDoc property value: {(flowDoc != null ? "not null" : "null")}");
                }

                // Clean up temp file after a delay to ensure it's been read
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(1000); // Wait 1 second
                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                            Console.WriteLine($"[RTF] Temp file deleted: {tempFile}");
                        }
                    }
                    catch (Exception delEx)
                    {
                        Console.WriteLine($"[RTF] Error deleting temp file: {delEx.Message}");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                // If RTF loading fails, fall back to stripped text
                Console.WriteLine($"[RTF] ERROR: Failed to load RTF: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[RTF] Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[RTF] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    Console.WriteLine($"[RTF] Inner exception stack trace: {ex.InnerException.StackTrace}");
                }

                // Log the full exception details for debugging
                Console.WriteLine($"[RTF] Full exception details: {ex}");

                // Fall back to stripped text (matches Python behavior - Python strips RTF because tkinter can't render it)
                viewModel.IsRtfContent = false;
                try
                {
                    string stripped = RtfStripper.StripRtf(rtfContent);
                    Console.WriteLine($"[RTF] Falling back to stripped text, length: {stripped.Length}");
                    // Set as plain text content - not as a log entry
                    // This matches Python's set_stripped_rtf_text behavior
                    viewModel.ClearLogText();
                    viewModel.AddLogEntry(stripped, KOTORModSync.Logger.LogType.Note);
                }
                catch (Exception stripEx)
                {
                    Console.WriteLine($"[RTF] ERROR: Failed to strip RTF: {stripEx.Message}");
                    viewModel.IsRtfContent = false;
                    viewModel.AddLogEntry("Failed to load RTF content. Please check the console for details.", KOTORModSync.Logger.LogType.Error);
                }
            }
        }

        /// <summary>
        /// Formats log text with colors and font sizes matching Python's tkinter text tags exactly.
        /// Matches Python's set_text_font and tag_configure behavior.
        /// </summary>
        private void FormatLogText()
        {
            if (_logTextBlock is null || !(DataContext is MainWindowViewModel viewModel))
            {
                return;
            }

            // Clear existing inlines
            _logTextBlock.Inlines.Clear();

            // Base font size: 9 (matching Python's font_obj.configure(size=9))
            // Bold font size: 10 (matching Python's bold_font.configure(size=10, weight="bold"))
            const int baseFontSize = 9;
            const int boldFontSize = 10;

            // Format each log entry with appropriate colors and font weights
            // Python tag colors:
            // DEBUG: #6495ED (Cornflower Blue)
            // INFO: #000000 (Black)
            // WARNING: #CC4E00 (Orange) with background #FFF3E0, bold font
            // ERROR: #DC143C (Firebrick) with bold font
            // CRITICAL: #FFFFFF (White) on background #8B0000 (Dark Red) with bold font

            foreach (FormattedLogEntry entry in viewModel.GetLogEntries())
            {
                var run = new Run(entry.Message + Environment.NewLine);

                switch (entry.TagName)
                {
                    case "DEBUG":
                        run.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6495ED"));
                        run.FontSize = baseFontSize;
                        break;
                    case "INFO":
                        run.Foreground = Avalonia.Media.Brushes.Black;
                        run.FontSize = baseFontSize;
                        break;
                    case "WARNING":
                        run.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC4E00"));
                        run.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFF3E0"));
                        run.FontWeight = Avalonia.Media.FontWeight.Bold;
                        run.FontSize = boldFontSize;
                        break;
                    case "ERROR":
                        run.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DC143C"));
                        run.FontWeight = Avalonia.Media.FontWeight.Bold;
                        run.FontSize = boldFontSize;
                        break;
                    case "CRITICAL":
                        run.Foreground = Avalonia.Media.Brushes.White;
                        run.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8B0000"));
                        run.FontWeight = Avalonia.Media.FontWeight.Bold;
                        run.FontSize = boldFontSize;
                        break;
                    default:
                        run.Foreground = Avalonia.Media.Brushes.Black;
                        run.FontSize = baseFontSize;
                        break;
                }

                _logTextBlock.Inlines.Add(run);
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // Unsubscribe from events
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            base.OnClosing(e);
        }
    }
}
