using System;
using System.Collections.Generic;

namespace Paperhome.Models
{
    public class DocumentRecord
    {
        public int Id { get; set; }
        
        // Оригинальное имя файла ОС
        public string OriginalFileName { get; set; } = string.Empty;
        
        // Сгенерированное ИИ имя
        public string AiGeneratedName { get; set; } = string.Empty;
        
        // Путь относительно корня локального хранилища
        public string RelativePath { get; set; } = string.Empty;
        
        // Развернутая выжимка (summary) от ИИ
        public string Summary { get; set; } = string.Empty;
        
        public long FileSizeBytes { get; set; }
        
        public DateTime AddedAt { get; set; } = DateTime.Now;

        // Связь с тегами (Многие-ко-многим)
        public List<TagRecord> Tags { get; set; } = new();
    }
}
