using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Messaging;
using Paperhome.Messages;
using Paperhome.Services;

namespace Paperhome.Views
{
    public partial class MainWindow : Window
    {
        private readonly ArchiveService _archiveService;
        private int? _selectedDocumentId = null;

        public MainWindow()
        {
            InitializeComponent();
            _archiveService = new ArchiveService();

            // Подписываемся на выделение файла в дереве
            WeakReferenceMessenger.Default.Register<DocumentSelectedMessage>(this, (r, m) => 
            {
                _selectedDocumentId = m.DocumentId;
            });
        }

        // Этот метод позволяет двигать окно за шапку (TitleBar)
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        // Сворачивание окна
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // Закрытие приложения
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "ДОБАВИТЬ ДОКУМЕНТ В АРХИВ",
                Filter = "Поддерживаемые (*.pdf, *.doc, *.docx, *.txt, *.md, *.png, *.jpg)|*.pdf;*.doc;*.docx;*.txt;*.md;*.png;*.jpg;*.jpeg|Документы PDF|*.pdf|Тексты и Word|*.doc;*.docx;*.txt;*.md|Изображения|*.png;*.jpg;*.jpeg"
            };

            if (dlg.ShowDialog() == true)
            {
                _archiveService.AddFile(dlg.FileName);
                // Оповещаем другие компоненты (например ArchiveView), что архив обновился
                WeakReferenceMessenger.Default.Send(new ArchiveUpdatedMessage());
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDocumentId.HasValue)
            {
                _archiveService.DeleteFile(_selectedDocumentId.Value);
                _selectedDocumentId = null;
                // Оповещаем о необходимости перерисовки
                WeakReferenceMessenger.Default.Send(new ArchiveUpdatedMessage());
            }
            else
            {
                // Временно используем MessageBox, далее можно заменить на выезжающую телетайп-ленту
                System.Windows.MessageBox.Show("Сначала выберите файл в архиве для удаления.", "ОШИБКА", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}