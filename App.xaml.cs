using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Paperhome.Data;
using Paperhome.Services;
using Paperhome.Views;

namespace Paperhome;

public partial class App : System.Windows.Application
{
    private static readonly string DbPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "paperwork.db");
    private static readonly string DbEncPath = DbPath + ".enc";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        EncryptDb();
        EncryptionService.Current.Lock();
        base.OnExit(e);
    }

    // ── Блокировка: шифрует БД и сбрасывает ключ ─────────────────────────────

    public static void LockAndEncrypt()
    {
        EncryptDb();
        EncryptionService.Current.Lock();
    }

    // ── Вызывается после ввода верного пин-кода ──────────────────────────────

    public static void SetupAfterUnlock()
    {
        DecryptDb();

        using var db = new PaperworkDbContext();
        db.Database.EnsureCreated();
        TryExec(db, "ALTER TABLE Documents ADD COLUMN AiGeneratedName TEXT NOT NULL DEFAULT ''");
        TryExec(db, "ALTER TABLE Documents ADD COLUMN Summary          TEXT NOT NULL DEFAULT ''");
        TryExec(db, "ALTER TABLE Documents ADD COLUMN FileSizeBytes    INTEGER NOT NULL DEFAULT 0");
        TryExec(db, "ALTER TABLE Documents ADD COLUMN AddedAt          TEXT NOT NULL DEFAULT ''");
        TryExec(db, @"
            CREATE TABLE IF NOT EXISTS Tags (
                Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT    NOT NULL DEFAULT ''
            )");
        TryExec(db, @"
            CREATE TABLE IF NOT EXISTS DocumentTags (
                DocumentsId INTEGER NOT NULL REFERENCES Documents(Id) ON DELETE CASCADE,
                TagsId      INTEGER NOT NULL REFERENCES Tags(Id)      ON DELETE CASCADE,
                PRIMARY KEY (DocumentsId, TagsId)
            )");

        MigrateFilesToEncrypted();
    }

    // ── БД ───────────────────────────────────────────────────────────────────

    private static void DecryptDb()
    {
        if (!File.Exists(DbEncPath)) return;
        if (File.Exists(DbPath)) File.Delete(DbPath);
        File.WriteAllBytes(DbPath, EncryptionService.Current.DecryptFile(DbEncPath));
    }

    private static void EncryptDb()
    {
        if (!File.Exists(DbPath) || !EncryptionService.Current.IsUnlocked) return;
        // Принудительно закрыть все пулируемые SQLite-соединения
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        EncryptionService.Current.EncryptFileTo(DbPath, DbEncPath);
        File.Delete(DbPath);
    }

    // ── Миграция файлов в зашифрованный формат ───────────────────────────────

    private static void MigrateFilesToEncrypted()
    {
        var storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "Files");
        if (!Directory.Exists(storagePath)) return;

        using var db = new PaperworkDbContext();
        var docs     = db.Documents.ToList();
        bool changed = false;

        foreach (var doc in docs)
        {
            if (doc.RelativePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase)) continue;
            var srcPath = Path.Combine(storagePath, doc.RelativePath);
            if (!File.Exists(srcPath)) continue;

            var dstPath = srcPath + ".enc";
            EncryptionService.Current.EncryptFileTo(srcPath, dstPath);
            File.Delete(srcPath);
            doc.RelativePath += ".enc";
            changed = true;
        }

        if (changed) db.SaveChanges();
    }

    private static void TryExec(PaperworkDbContext db, string sql)
    {
        try   { db.Database.ExecuteSqlRaw(sql); }
        catch { }
    }
}
