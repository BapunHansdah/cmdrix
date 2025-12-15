using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using cmdrix.Services;
using cmdrix.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;

namespace cmdrix
{
    public partial class MainWindow : Window
    {
        private readonly CommandProcessor _commandProcessor;
        private readonly NotesService _notesService;
        private readonly ScreenshotService _screenshotService;
        private readonly ConfigService _configService;
        private CommandPreview? _currentPreview;
        private List<string> _commandHistory;
        private int _historyIndex;
        private string? _editingNoteId;
        private readonly List<string> _availableCommands = new List<string>
        {
            "/command",
            "/notes",
            "/notes list",
            "/screenshot",
            "/config",
            "/config apikey",
            "/config opacity",
            "/config dir",
            "/clear",
            "/exit",
            "/quit",
            "/close",
            "/help"
        };

        // Global hotkey registration
        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_SPACE = 0x20;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _windowHandle;
        private HwndSource _source;

        public MainWindow()
        {
            InitializeComponent();

            _commandProcessor = new CommandProcessor();
            _notesService = new NotesService();
            _screenshotService = new ScreenshotService();
            _configService = new ConfigService();
            _commandHistory = new List<string>();
            _historyIndex = -1;

            LoadConfiguration();
            InputBox.Focus();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Get window handle and register global hotkey
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);

            // Register Ctrl+Space as global hotkey
            RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL, VK_SPACE);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unregister hotkey when window closes
            _source.RemoveHook(HwndHook);
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
            base.OnClosed(e);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                // Toggle window visibility
                if (this.Visibility == Visibility.Visible)
                {
                    this.Visibility = Visibility.Hidden;
                }
                else
                {
                    this.Visibility = Visibility.Visible;
                    this.Activate();
                    InputBox.Focus();
                }
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void LoadConfiguration()
        {
            var config = _configService.LoadConfig();
            this.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(
                    config.BackgroundOpacity,
                    config.BackgroundColor.R,
                    config.BackgroundColor.G,
                    config.BackgroundColor.B
                )
            );
        }

        private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle autocomplete navigation first
            if (AutocompletePopup.IsOpen)
            {
                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    if (AutocompleteList.SelectedIndex < AutocompleteList.Items.Count - 1)
                    {
                        AutocompleteList.SelectedIndex++;
                    }
                    else
                    {
                        AutocompleteList.SelectedIndex = 0;
                    }
                    AutocompleteList.ScrollIntoView(AutocompleteList.SelectedItem);
                    return;
                }
                else if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    if (AutocompleteList.SelectedIndex > 0)
                    {
                        AutocompleteList.SelectedIndex--;
                    }
                    else
                    {
                        AutocompleteList.SelectedIndex = AutocompleteList.Items.Count - 1;
                    }
                    AutocompleteList.ScrollIntoView(AutocompleteList.SelectedItem);
                    return;
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (AutocompleteList.SelectedItem != null)
                    {
                        InputBox.Text = AutocompleteList.SelectedItem.ToString();
                        InputBox.CaretIndex = InputBox.Text.Length;
                        AutocompletePopup.IsOpen = false;
                    }
                    return;
                }
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ProcessInput();
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                NavigateHistory(-1);
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                NavigateHistory(1);
            }
            else if (e.Key == Key.Escape)
            {
                if (PreviewPanel.Visibility == Visibility.Visible)
                {
                    CancelPreview();
                }
                else
                {
                    InputBox.Clear();
                    AutocompletePopup.IsOpen = false;
                }
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                HandleTabCompletion();
            }
        }

        private async System.Threading.Tasks.Task ProcessInput()
        {
            var input = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            _commandHistory.Add(input);
            _historyIndex = _commandHistory.Count;

            AddToOutput($"> {input}");
            InputBox.Clear();

            try
            {
                // Configuration commands
                if (input.StartsWith("/config"))
                {
                    await HandleConfigCommand(input);
                }
                else if (input.StartsWith("/command"))
                {
                    var query = input.Substring(8).Trim();
                    var preview = await _commandProcessor.GenerateCommandPreview(query);
                    ShowPreview(preview);
                }
                else if (input.StartsWith("/notes"))
                {
                    await HandleNotesCommand(input);
                }
                else if (input.StartsWith("/screenshot"))
                {
                    var question = input.Substring(11).Trim();
                    AddToOutput("📸 Taking screenshot...");
                    var result = await _screenshotService.CaptureAndAnalyze(question);
                    AddToOutput(result);
                }
                else if (input == "/help")
                {
                    ShowHelp();
                }
                else if (input == "/clear")
                {
                    OutputText.Text = "";
                    OutputScroll.Visibility = System.Windows.Visibility.Collapsed;
                    this.Height = 110;
                }
                else if (input == "/exit" || input == "/quit" || input == "/close")
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    // Regular chat with AI
                    var response = await _commandProcessor.ChatWithAI(input);
                    AddToOutput(response);
                }
            }
            catch (System.Exception ex)
            {
                AddToOutput($"❌ Error: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task HandleNotesCommand(string input)
        {
            var parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1 || parts[1].ToLower() == "list")
            {
                // Show notes list modal
                ShowNotesListModal();
                return;
            }

            // Quick note creation
            var noteContent = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(noteContent))
            {
                AddToOutput("❌ Note content cannot be empty. Use /notes to open notes list.");
                return;
            }
            await _notesService.AddNote(noteContent);
            AddToOutput("✓ Note saved");
        }

        private void ShowNotesListModal()
        {
            RefreshNotesList();
            NotesListModal.Visibility = Visibility.Visible;
            InputBox.IsEnabled = false;
            NotesSearchBox.Focus();
        }

        private void CloseNotesListModal_Click(object sender, RoutedEventArgs e)
        {
            NotesListModal.Visibility = Visibility.Collapsed;
            InputBox.IsEnabled = true;
            InputBox.Focus();
            NotesSearchBox.Clear();
        }

        private void RefreshNotesList(string searchQuery = "")
        {
            var notes = _notesService.SearchNotes(searchQuery);
            var noteViewModels = notes.Select(n => new NoteViewModel
            {
                Id = n.Id,
                Content = n.Content.Length > 200 ? n.Content.Substring(0, 200) + "..." : n.Content,
                TagsDisplay = n.Tags.Any() ? string.Join(" ", n.Tags.Select(t => "#" + t)) : "",
                CreatedAtDisplay = n.CreatedAt.ToString("MMM dd, yyyy HH:mm")
            }).ToList();

            NotesListView.ItemsSource = noteViewModels;
        }

        private void NotesSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshNotesList(NotesSearchBox.Text);
        }

        private void EditNoteFromList_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var noteId = button?.Tag as string;

            if (noteId != null)
            {
                var note = _notesService.GetNoteById(noteId);
                if (note != null)
                {
                    NotesListModal.Visibility = Visibility.Collapsed;
                    OpenNoteEditor(note.Id, note.Content);
                }
            }
        }

        private async void DeleteNoteFromList_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var noteId = button?.Tag as string;

            if (noteId != null)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to delete this note?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    await _notesService.DeleteNote(noteId);
                    RefreshNotesList(NotesSearchBox.Text);
                }
            }
        }

        private void NewNoteFromList_Click(object sender, RoutedEventArgs e)
        {
            NotesListModal.Visibility = Visibility.Collapsed;
            OpenNoteEditor(null, "");
        }

        private void OpenNoteEditor(string? noteId, string content)
        {
            _editingNoteId = noteId;
            NoteEditorTitle.Text = noteId == null ? "New Note" : $"Edit Note ({noteId.Substring(0, 8)})";
            NoteEditorTextBox.Text = content;
            NoteEditorPanel.Visibility = Visibility.Visible;
            InputBox.IsEnabled = false;
            NoteEditorTextBox.Focus();
            NoteEditorTextBox.CaretIndex = content.Length;
        }

        private async void SaveNoteButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentNote();
        }

        private async System.Threading.Tasks.Task SaveCurrentNote()
        {
            var content = NoteEditorTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                AddToOutput("❌ Note content cannot be empty");
                return;
            }

            if (_editingNoteId != null)
            {
                // Update existing note
                await _notesService.UpdateNote(_editingNoteId, content);
                AddToOutput($"✓ Note updated: {_editingNoteId.Substring(0, 8)}");
            }
            else
            {
                // Create new note
                await _notesService.AddNote(content);
                AddToOutput("✓ Note saved");
            }

            CloseNoteEditor();
        }

        private void CancelNoteButton_Click(object sender, RoutedEventArgs e)
        {
            CloseNoteEditor();
        }

        private void CloseNoteEditor()
        {
            NoteEditorPanel.Visibility = Visibility.Collapsed;
            NoteEditorTextBox.Clear();
            InputBox.IsEnabled = true;
            InputBox.Focus();
            _editingNoteId = null;
        }

        private async System.Threading.Tasks.Task HandleConfigCommand(string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                // Show current config
                var config = _configService.LoadConfig();
                AddToOutput("Current Configuration:");
                AddToOutput($"  API Key: {(string.IsNullOrEmpty(config.GeminiApiKey) ? "Not set" : "***" + config.GeminiApiKey.Substring(Math.Max(0, config.GeminiApiKey.Length - 4)))}");
                AddToOutput($"  Opacity: {config.BackgroundOpacity} ({(config.BackgroundOpacity / 255.0 * 100):F0}%)");
                AddToOutput($"  Directory: {config.CurrentDirectory}");
                AddToOutput("\nUsage:");
                AddToOutput("  /config apikey YOUR_KEY");
                AddToOutput("  /config opacity 0-255");
                AddToOutput("  /config dir PATH");
                return;
            }

            var command = parts[1].ToLower();

            switch (command)
            {
                case "apikey":
                    if (parts.Length < 3)
                    {
                        AddToOutput("❌ Usage: /config apikey YOUR_API_KEY");
                        return;
                    }
                    _configService.UpdateApiKey(parts[2]);
                    AddToOutput("✓ API key updated. Restart app to apply.");
                    break;

                case "opacity":
                    if (parts.Length < 3 || !byte.TryParse(parts[2], out var opacity))
                    {
                        AddToOutput("❌ Usage: /config opacity 0-255");
                        return;
                    }
                    _configService.UpdateBackgroundOpacity(opacity);
                    UpdateOpacity(opacity);
                    AddToOutput($"✓ Opacity set to {opacity} ({(opacity / 255.0 * 100):F0}%)");
                    break;

                case "dir":
                case "directory":
                    if (parts.Length < 3)
                    {
                        AddToOutput("❌ Usage: /config dir PATH");
                        return;
                    }
                    var dir = string.Join(" ", parts.Skip(2));
                    if (!System.IO.Directory.Exists(dir))
                    {
                        AddToOutput($"❌ Directory does not exist: {dir}");
                        return;
                    }
                    _configService.UpdateCurrentDirectory(dir);
                    System.IO.Directory.SetCurrentDirectory(dir);
                    AddToOutput($"✓ Current directory set to: {dir}");
                    break;

                default:
                    AddToOutput($"❌ Unknown config option: {command}");
                    AddToOutput("Available options: apikey, opacity, dir");
                    break;
            }
        }

        private void ShowHelp()
        {
            AddToOutput("cmdrix - AI-Powered Terminal Assistant\n");
            AddToOutput("Commands:");
            AddToOutput("  /command [query]     - Generate terminal commands from natural language");
            AddToOutput("  /notes [content]     - Quick note or open notes list");
            AddToOutput("  /notes list          - Open notes list modal");
            AddToOutput("  /screenshot [query]  - Capture screen and analyze");
            AddToOutput("  /config              - View current configuration");
            AddToOutput("  /config apikey KEY   - Set Gemini API key");
            AddToOutput("  /config opacity 0-255 - Set background transparency");
            AddToOutput("  /config dir PATH     - Set working directory");
            AddToOutput("  /clear               - Clear output");
            AddToOutput("  /exit                - Exit cmdrix (or /quit, /close)");
            AddToOutput("  /help                - Show this help");
            AddToOutput("\nKeyboard Shortcuts:");
            AddToOutput("  Enter              - Execute command");
            AddToOutput("  Ctrl+Space         - Hide/Show window (GLOBAL)");
            AddToOutput("  ↑/↓                - Navigate command history");
            AddToOutput("  Esc                - Cancel/Clear");
            AddToOutput("  Tab                - Accept autocomplete");
            AddToOutput("  Click & Drag       - Move window");
            AddToOutput("\nChat:");
            AddToOutput("  Type anything without / to chat with AI");
        }

        private void UpdateOpacity(byte opacity)
        {
            var border = (System.Windows.Controls.Border)this.Content;
            var color = ((System.Windows.Media.SolidColorBrush)border.Background).Color;
            border.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(opacity, color.R, color.G, color.B)
            );
        }

        private void ShowPreview(CommandPreview preview)
        {
            _currentPreview = preview;
            PreviewCommandText.Text = preview.Command;

            // Set safety indicator
            switch (preview.SafetyLevel)
            {
                case SafetyLevel.Safe:
                    SafetyIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 170, 0));
                    SafetyText.Text = "✓ SAFE";
                    break;
                case SafetyLevel.Warning:
                    SafetyIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 170, 0));
                    SafetyText.Text = "⚠ WARNING";
                    break;
                case SafetyLevel.Danger:
                    SafetyIndicator.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 68, 68));
                    SafetyText.Text = "⚠ DANGER";
                    break;
            }

            PreviewPanel.Visibility = Visibility.Visible;
            InputBox.IsEnabled = false;
        }

        private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreview != null)
            {
                AddToOutput($"Executing: {_currentPreview.Command}");
                var result = await _commandProcessor.ExecuteCommand(_currentPreview);
                AddToOutput(result);
                CancelPreview();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelPreview();
        }

        private void CancelPreview()
        {
            PreviewPanel.Visibility = Visibility.Collapsed;
            InputBox.IsEnabled = true;
            InputBox.Focus();
            _currentPreview = null;
        }

        private void AddToOutput(string message)
        {
            if (OutputScroll.Visibility == Visibility.Collapsed)
            {
                OutputScroll.Visibility = Visibility.Visible;
                this.Height = 430;
            }

            OutputText.Text += $"{message}\n";
            OutputScroll.ScrollToBottom();
        }

        private void NavigateHistory(int direction)
        {
            if (_commandHistory.Count == 0) return;

            _historyIndex += direction;
            _historyIndex = System.Math.Max(0, System.Math.Min(_historyIndex, _commandHistory.Count));

            if (_historyIndex < _commandHistory.Count)
            {
                InputBox.Text = _commandHistory[_historyIndex];
                InputBox.CaretIndex = InputBox.Text.Length;
            }
            else
            {
                InputBox.Clear();
            }
        }

        private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = InputBox.Text;

            // Command autocomplete
            if (text.StartsWith("/") && !text.Contains(" "))
            {
                var matches = _availableCommands
                    .Where(cmd => cmd.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Any() && text.Length > 1)
                {
                    ShowAutocomplete(matches);
                }
                else
                {
                    AutocompletePopup.IsOpen = false;
                }
            }
            // Config sub-commands autocomplete
            else if (text.StartsWith("/config "))
            {
                var afterConfig = text.Substring(8);
                // Only show suggestions if there's no space after the subcommand (still typing it)
                if (!afterConfig.Contains(' '))
                {
                    var configCommands = new List<string> { "/config apikey", "/config opacity", "/config dir" };
                    var matches = configCommands
                        .Where(cmd => cmd.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matches.Any())
                    {
                        ShowAutocomplete(matches);
                    }
                    else
                    {
                        AutocompletePopup.IsOpen = false;
                    }
                }
                else
                {
                    AutocompletePopup.IsOpen = false;
                }
            }
            else
            {
                AutocompletePopup.IsOpen = false;
            }
        }

        private void HandleTabCompletion()
        {
            var text = InputBox.Text;

            // If autocomplete popup is open, use selected item
            if (AutocompletePopup.IsOpen && AutocompleteList.Items.Count > 0)
            {
                var selectedItem = AutocompleteList.SelectedItem?.ToString()
                    ?? AutocompleteList.Items[0]?.ToString();

                if (selectedItem != null)
                {
                    InputBox.Text = selectedItem;
                    InputBox.CaretIndex = InputBox.Text.Length;
                    AutocompletePopup.IsOpen = false;
                }
                return;
            }

            // Command completion without popup
            if (text.StartsWith("/") && !text.Contains(" "))
            {
                var matches = _availableCommands
                    .Where(cmd => cmd.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(cmd => cmd.Length)
                    .ToList();

                if (matches.Any())
                {
                    // Get the shortest match (most likely what user wants)
                    InputBox.Text = matches.First();
                    InputBox.CaretIndex = InputBox.Text.Length;
                }
            }
        }

        private void ShowAutocomplete(List<string> suggestions)
        {
            if (suggestions.Any())
            {
                AutocompleteList.ItemsSource = suggestions;
                AutocompleteList.SelectedIndex = 0;  // Select first item by default
                AutocompletePopup.IsOpen = true;
            }
            else
            {
                AutocompletePopup.IsOpen = false;
            }
        }

        private void AutocompleteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Selection changed, but don't auto-apply (wait for Enter or Tab)
        }

        private void AutocompleteList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (AutocompleteList.SelectedItem != null)
                {
                    InputBox.Text = AutocompleteList.SelectedItem.ToString();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    AutocompletePopup.IsOpen = false;
                    InputBox.Focus();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                AutocompletePopup.IsOpen = false;
                InputBox.Focus();
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Note Editor shortcuts
            if (NoteEditorPanel.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    SaveCurrentNote();
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CloseNoteEditor();
                }
                return;
            }

            // Notes List Modal shortcuts
            if (NotesListModal.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CloseNotesListModal_Click(null, null);
                }
                return;
            }

            // Note: Ctrl+Space is now handled globally via RegisterHotKey
            // No need to handle it here anymore
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window by clicking anywhere on the border
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Hidden;
        }
    }

    // View model for displaying notes in the list
    public class NoteViewModel
    {
        public string Id { get; set; }
        public string Content { get; set; }
        public string TagsDisplay { get; set; }
        public string CreatedAtDisplay { get; set; }
    }
}