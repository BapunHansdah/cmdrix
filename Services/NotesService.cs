using System.IO;
using System.Text.Json;
using cmdrix.Models;

namespace cmdrix.Services
{
    public class NotesService
    {
        private readonly string _notesFilePath;
        private List<Note> _notes;

        public NotesService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "cmdrix"
            );
            Directory.CreateDirectory(appDataPath);
            _notesFilePath = Path.Combine(appDataPath, "notes.json");
            _notes = LoadNotes();
        }

        public async Task AddNote(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            var note = new Note
            {
                Content = content,
                CreatedAt = DateTime.Now,
                Tags = ExtractTags(content)
            };

            _notes.Add(note);
            await SaveNotes();
        }

        public async Task UpdateNote(string noteId, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            var note = _notes.FirstOrDefault(n => n.Id == noteId);
            if (note == null)
                throw new Exception($"Note not found: {noteId}");

            note.Content = content;
            note.Tags = ExtractTags(content);
            await SaveNotes();
        }

        public Note? GetNoteById(string noteId)
        {
            return _notes.FirstOrDefault(n => n.Id.StartsWith(noteId, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<string>> GetAutocompleteSuggestions(string partial)
        {
            if (string.IsNullOrWhiteSpace(partial))
                return new List<string>();

            // Get suggestions from recent notes
            var suggestions = _notes
                .Where(n => n.Content.Contains(partial, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .Select(n => n.Content)
                .ToList();

            // Add tag-based suggestions
            var tagSuggestions = _notes
                .SelectMany(n => n.Tags)
                .Distinct()
                .Where(t => t.Contains(partial, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .Select(t => $"#{t}")
                .ToList();

            suggestions.AddRange(tagSuggestions);
            return suggestions.Distinct().ToList();
        }

        public List<Note> SearchNotes(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _notes.OrderByDescending(n => n.CreatedAt).ToList();

            return _notes
                .Where(n => n.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           n.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
        }

        public async Task DeleteNote(string noteId)
        {
            _notes.RemoveAll(n => n.Id == noteId);
            await SaveNotes();
        }

        private List<Note> LoadNotes()
        {
            try
            {
                if (File.Exists(_notesFilePath))
                {
                    var json = File.ReadAllText(_notesFilePath);
                    return JsonSerializer.Deserialize<List<Note>>(json) ?? new List<Note>();
                }
            }
            catch (Exception)
            {
                // If loading fails, start with empty list
            }
            return new List<Note>();
        }

        private async Task SaveNotes()
        {
            try
            {
                var json = JsonSerializer.Serialize(_notes, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_notesFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save notes: {ex.Message}");
            }
        }

        private List<string> ExtractTags(string content)
        {
            var words = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return words
                .Where(w => w.StartsWith("#") && w.Length > 1)
                .Select(w => w.Substring(1).ToLower())
                .Distinct()
                .ToList();
        }
    }
}