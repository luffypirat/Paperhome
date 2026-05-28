using System;
using System.IO;
using System.Security.Cryptography;

namespace Paperhome.Services
{
    public class EncryptionService
    {
        public static readonly EncryptionService Current = new();
        private EncryptionService() { }

        private byte[]? _key;
        public bool IsUnlocked => _key != null;

        private const int SaltSize   = 32;
        private const int KeySize    = 32;  // AES-256
        private const int Iterations = 200_000;

        public bool HasPassword(AppSettings s) => !string.IsNullOrEmpty(s.PasswordSalt);

        // Первый запуск: устанавливает пин, выводит ключ
        public void SetPassword(string pin, AppSettings settings)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            _key = DeriveKey(pin, salt);
            settings.PasswordSalt = Convert.ToBase64String(salt);
            settings.PasswordHash = Convert.ToBase64String(SHA256.HashData(_key));
            settings.Save();
        }

        // Разблокировка: проверяет пин и сохраняет ключ
        public bool TryUnlock(string pin, AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.PasswordSalt)) return false;
            var salt = Convert.FromBase64String(settings.PasswordSalt);
            var key  = DeriveKey(pin, salt);
            var hash = Convert.FromBase64String(settings.PasswordHash);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(key), hash))
                return false;
            _key = key;
            return true;
        }

        public void Lock()
        {
            if (_key != null) CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }

        // Шифрование: IV (16 байт) + зашифрованные данные
        public byte[] Encrypt(byte[] plain)
        {
            if (_key == null) throw new InvalidOperationException("Не разблокировано");
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();
            using var ms = new MemoryStream();
            ms.Write(aes.IV);
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                cs.Write(plain);
            return ms.ToArray();
        }

        // Дешифрование: первые 16 байт — IV
        public byte[] Decrypt(byte[] cipher)
        {
            if (_key == null) throw new InvalidOperationException("Не разблокировано");
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV  = cipher[..16];
            using var input  = new MemoryStream(cipher, 16, cipher.Length - 16);
            using var cs     = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            return output.ToArray();
        }

        public void EncryptFileTo(string srcPath, string dstPath)
            => File.WriteAllBytes(dstPath, Encrypt(File.ReadAllBytes(srcPath)));

        public byte[] DecryptFile(string encPath)
            => Decrypt(File.ReadAllBytes(encPath));

        // Дешифрует во временный файл; вызывающий код обязан удалить его
        public string DecryptToTemp(string encPath, string originalExt)
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"ph_{Guid.NewGuid():N}{originalExt}");
            File.WriteAllBytes(tmp, DecryptFile(encPath));
            return tmp;
        }

        private static byte[] DeriveKey(string pin, byte[] salt)
            => Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
