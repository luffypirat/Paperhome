using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Paperhome.Messages;
using Paperhome.Services;
using Paperhome.Models;

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

            // Подписываемся на события изменения архива со стороны MainWindow
            WeakReferenceMessenger.Default.Register<ArchiveUpdatedMessage>(this, (r, m) => 
            {
                Dispatcher.Invoke(() => LoadFiles());
            });
        }

        private void LoadFiles()
        {
            FilesTree.Items.Clear();

            // Создаем заголовок корневого узла
            var rootHeader = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var rootIcon = new MahApps.Metro.IconPacks.PackIconPixelartIcons { Kind = MahApps.Metro.IconPacks.PackIconPixelartIconsKind.Folder, Width = 14, Height = 14, Margin = new Thickness(0,0,6,0), VerticalAlignment = VerticalAlignment.Center };
            var rootText = new TextBlock { Text = "ЛОКАЛЬНАЯ БАЗА [OS_FS]", VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetEdgeMode(rootIcon, EdgeMode.Aliased);
            rootHeader.Children.Add(rootIcon);
            rootHeader.Children.Add(rootText);

            var root = new TreeViewItem 
            { 
                Header = rootHeader, 
                IsExpanded = true,
                Foreground = new SolidColorBrush(Colors.Black),
                FontWeight = FontWeights.Bold
            };
    
            var docs = _archiveService.GetAll();
            foreach (var doc in docs)
            {
                // ИСПРАВЛЕНИЕ: Создаем НОВУЮ StackPanel для КАЖДОГО файла
                var itemHeader = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        
                var itemIcon = new MahApps.Metro.IconPacks.PackIconPixelartIcons { Kind = MahApps.Metro.IconPacks.PackIconPixelartIconsKind.File, Width = 14, Height = 14, Margin = new Thickness(0,0,6,0), VerticalAlignment = VerticalAlignment.Center };
                var itemText = new TextBlock { Text = doc.OriginalFileName, VerticalAlignment = VerticalAlignment.Center };
        
                RenderOptions.SetEdgeMode(itemIcon, EdgeMode.Aliased);
        
                itemHeader.Children.Add(itemIcon);
                itemHeader.Children.Add(itemText);

                var item = new TreeViewItem 
                { 
                    Header = itemHeader, 
                    Tag = doc 
                };
                root.Items.Add(item);
            }

            FilesTree.Items.Add(root);
        }
        private void FilesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is DocumentRecord doc)
            {
                // Отправляем сообщение, что выбран конкретный документ
                WeakReferenceMessenger.Default.Send(new DocumentSelectedMessage(doc.Id));
            }
            else
            {
                // Если выбран корень дерева или пусто - снимаем выделение
                WeakReferenceMessenger.Default.Send(new DocumentSelectedMessage(0));
            }
        }
    }
}






