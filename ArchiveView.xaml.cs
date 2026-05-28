using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Paperhome.Messages;
using Paperhome.Models;
using Paperhome.Services;

namespace Paperhome.Views
{
    public partial class ArchiveView : System.Windows.Controls.UserControl
    {
        private readonly ArchiveService _archiveService;

        public ArchiveView()
        {
            InitializeComponent();
            _archiveService = new ArchiveService();
            LoadFiles();

            WeakReferenceMessenger.Default.Register<ArchiveUpdatedMessage>(this, (_, _) =>
                Dispatcher.Invoke(() => LoadFiles(TxtSearch.Text.Trim())));
        }

        // ── Загрузка дерева ──────────────────────────────────────────────────

        private void LoadFiles(string filter = "")
        {
            if (!Paperhome.Services.EncryptionService.Current.IsUnlocked) return;
            FilesTree.Items.Clear();

            var rootHeader = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var rootIcon = new MahApps.Metro.IconPacks.PackIconPixelartIcons
            {
                Kind = MahApps.Metro.IconPacks.PackIconPixelartIconsKind.Folder,
                Width = 14, Height = 14,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var rootText = new TextBlock
            {
                Text = "ЛОКАЛЬНАЯ БАЗА [OS_FS]",
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetEdgeMode(rootIcon, EdgeMode.Aliased);
            rootHeader.Children.Add(rootIcon);
            rootHeader.Children.Add(rootText);

            var root = new TreeViewItem
            {
                Header = rootHeader,
                IsExpanded = true,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold
            };

            var docs = _archiveService.GetAll();

            foreach (var doc in docs)
            {
                if (!string.IsNullOrEmpty(filter) && !MatchesFilter(doc, filter))
                    continue;

                var itemHeader = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var itemIcon = new MahApps.Metro.IconPacks.PackIconPixelartIcons
                {
                    Kind = MahApps.Metro.IconPacks.PackIconPixelartIconsKind.File,
                    Width = 14, Height = 14,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var displayName = !string.IsNullOrEmpty(doc.AiGeneratedName)
                                  && doc.AiGeneratedName != "ОЖИДАЕТ АНАЛИЗА..."
                    ? doc.AiGeneratedName
                    : doc.OriginalFileName;
                var itemText = new TextBlock { Text = displayName, VerticalAlignment = VerticalAlignment.Center };

                RenderOptions.SetEdgeMode(itemIcon, EdgeMode.Aliased);
                itemHeader.Children.Add(itemIcon);
                itemHeader.Children.Add(itemText);

                var item = new TreeViewItem { Header = itemHeader, Tag = doc };
                root.Items.Add(item);
            }

            FilesTree.Items.Add(root);
        }

        private static bool MatchesFilter(DocumentRecord doc, string filter) =>
            (doc.OriginalFileName ?? "").Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            (doc.AiGeneratedName ?? "").Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
            (doc.Summary        ?? "").Contains(filter, System.StringComparison.OrdinalIgnoreCase);

        // ── Выбор документа ──────────────────────────────────────────────────

        private void FilesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is DocumentRecord doc)
                WeakReferenceMessenger.Default.Send(new DocumentSelectedMessage(doc.Id));
            else
                WeakReferenceMessenger.Default.Send(new DocumentSelectedMessage(0));
        }

        // ── Поиск ────────────────────────────────────────────────────────────

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchPanel.Visibility == Visibility.Collapsed)
            {
                SearchPanel.Visibility = Visibility.Visible;
                TxtSearch.Focus();
            }
            else
            {
                SearchPanel.Visibility = Visibility.Collapsed;
                TxtSearch.Text = "";
                LoadFiles();
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            LoadFiles(TxtSearch.Text.Trim());
        }
    }
}
