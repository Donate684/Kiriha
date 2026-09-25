using Avalonia.Controls;
using Avalonia.Input;
using Kiriha.Core.Domain.Models.Genres;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList
{
    public partial class AnimeListHeader : UserControl
    {
        public event System.EventHandler? ReleaseMapRequested;

        public AnimeListHeader()
        {
            InitializeComponent();
        }

        private void ReleaseMapButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ReleaseMapRequested?.Invoke(this, System.EventArgs.Empty);
        }

        public void SetReleaseMapButtonState(bool isActive)
        {
            var button = this.FindControl<Button>("ReleaseMapButton");
            if (button != null)
            {
                if (isActive)
                    button.Classes.Add("active");
                else
                    button.Classes.Remove("active");
            }
        }

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not AnimeListViewModel vm) return;

            var textBox = sender as TextBox;
            var text = textBox?.Text ?? string.Empty;

            if (e.Key == Key.Back && string.IsNullOrEmpty(text))
            {
                if (vm.SelectedGenres.Count > 0 || vm.SelectedFormats.Count > 0)
                {
                    vm.RemoveLastTagOrBackspace();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (vm.IsSuggestionsOpen)
                {
                    vm.IsSuggestionsOpen = false;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Tab || e.Key == Key.Enter)
            {
                if (vm.IsSuggestionsOpen && vm.TagSuggestions.Count > 0)
                {
                    var target = vm.SelectedSuggestion ?? vm.TagSuggestions[0];
                    vm.AddTagSuggestion(target);
                    e.Handled = true;
                }
                else if (!string.IsNullOrWhiteSpace(text))
                {
                    var clean = text.Trim();
                    if (clean.StartsWith('#') && clean.Length > 1)
                    {
                        var candidate = clean[1..];
                        if (Kiriha.Core.Domain.Models.Formats.FormatCatalog.TryFindFormat(candidate, out var f) && f != null)
                        {
                            vm.AddFormatTag(f);
                            e.Handled = true;
                        }
                        else if (GenreCatalog.TryFindGenre(candidate, out var g) && g != null)
                        {
                            vm.AddGenreTag(g);
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        if (Kiriha.Core.Domain.Models.Formats.FormatCatalog.TryFindFormat(clean, out var f) && f != null)
                        {
                            vm.AddFormatTag(f);
                            e.Handled = true;
                        }
                        else if (GenreCatalog.TryFindGenre(clean, out var g) && g != null)
                        {
                            vm.AddGenreTag(g);
                            e.Handled = true;
                        }
                    }
                }
            }
        }
    }
}
