using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
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
            var fileName    = Path.GetFileName(sourceFilePath);
            var ext         = Path.GetExtension(sourceFilePath);
            var newFileName = Guid.NewGuid().ToString("N") + ext + ".enc";
            var targetPath  = Path.Combine(_storagePath, newFileName);

            EncryptionService.Current.EncryptFileTo(sourceFilePath, targetPath);

            try
            {
                var record = new DocumentRecord
                {
                    OriginalFileName = fileName,
                    AiGeneratedName  = "ОЖИДАЕТ АНАЛИЗА...",
                    RelativePath     = newFileName,
                    FileSizeBytes    = new FileInfo(sourceFilePath).Length,
                    AddedAt          = DateTime.Now
                };

                using var db = new PaperworkDbContext();
                db.Documents.Add(record);
                db.SaveChanges();
                return record;
            }
            catch
            {
                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                throw;
            }
        }

        public void DeleteFile(int id)
        {
            using var db = new PaperworkDbContext();
            var record = db.Documents.Include(d => d.Tags).FirstOrDefault(d => d.Id == id);
            if (record == null) return;

            var fullPath = Path.Combine(_storagePath, record.RelativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);

            record.Tags.Clear();
            db.Documents.Remove(record);
            db.SaveChanges();
        }

        public List<DocumentRecord> GetAll()
        {
            using var db = new PaperworkDbContext();
            return db.Documents.OrderByDescending(d => d.AddedAt).ToList();
        }
    }
}
