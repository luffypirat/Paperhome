namespace Paperhome.Messages
{
    // Отправляется, когда архив обновился (добавлен/удален файл)
    public record ArchiveUpdatedMessage();

    // Отправляется при выделении файла в дереве архива
    public record DocumentSelectedMessage(int DocumentId);
}