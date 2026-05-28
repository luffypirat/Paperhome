using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Paperhome.Data;
using Paperhome.Models;

namespace Paperhome.Services
{
    public class ArchiveService
    {
        private readonly string _storagePath;

        public ArchiveService()
        {
            _storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "Files");
            if (!Directory.Exists(_storagePath))
                Directory.CreateDirectory(_storagePath);
        }

        public DocumentRecord AddFile(string sourceFilePath)
        {
            var fileName = Path.GetFileName(sourceFilePath);
            var ext = Path.GetExtension(sourceFilePath);
            // Генерируем уникальное имя для физического хранения
            var newFileName = Guid.NewGuid().ToString("N") + ext;
            var targetPath = Path.Combine(_storagePath, newFileName);

            // Копируем файл в наше защищенное хранилище
            File.Copy(sourceFilePath, targetPath, true);

            var record = new DocumentRecord
            {
                OriginalFileName = fileName,
                AiGeneratedName = "ОЖИДАЕТ АНАЛИЗА...",
                RelativePath = newFileName,
                FileSizeBytes = new FileInfo(targetPath).Length,
                AddedAt = DateTime.Now
            };

            using var db = new PaperworkDbContext();
            db.Documents.Add(record);
            db.SaveChanges(); // Сохраняем метаданные в SQLite

            return record;
        }

        public void DeleteFile(int id)
        {
            using var db = new PaperworkDbContext();
            var record = db.Documents.Find(id);
            if (record != null)
            {
                // Удаляем локальный физический файл
                var fullPath = Path.Combine(_storagePath, record.RelativePath);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
                
                // Удаляем запись из БД
                db.Documents.Remove(record);
                db.SaveChanges();
            }
        }

        public List<DocumentRecord> GetAll()
        {
            using var db = new PaperworkDbContext();
            return db.Documents.ToList();
        }
    }
}