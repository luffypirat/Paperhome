using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Paperhome.Models;

namespace Paperhome.Data
{
    public class PaperworkDbContext : DbContext
    {
        public DbSet<DocumentRecord> Documents { get; set; }
        public DbSet<TagRecord> Tags { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Настройка пути до локальной базы данных SQLite
            // Сохраняем в папке ArchiveData рядом с исполняемым файлом
            string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
            string dbFolder = Path.Combine(baseFolder, "ArchiveData");
            
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }
            
            string dbPath = Path.Combine(dbFolder, "paperwork.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Настройка связей, если потребуется
            base.OnModelCreating(modelBuilder);
        }
    }
}
