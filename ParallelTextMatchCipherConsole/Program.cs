using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ParallelTextMatchCipherConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Регулярное выражение, задаваемое пользователем 
            const string baseRegex = @"\d+";

            // Пароль для генерации секретного ключа
            const string password = "Пароль для генерации ключа";

            SymmetricAlgorithm algorithm = Aes.Create();
            byte[] iv = { 15, 122, 132, 5, 93, 198, 44, 31, 9, 39, 241, 49, 250, 188, 80, 7 };
            byte[] key;
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                key = sha256.ComputeHash(passwordBytes);
            }
            ICryptoTransform encryptor = algorithm.CreateEncryptor(key, iv);
            ICryptoTransform decryptor = algorithm.CreateDecryptor(key, iv);

            // Пример исходного текста
            string sourceText = "Ваш_исходный_1_текст_1_здесь.";

            // Разбиваем текст на блоки
            var textBlocks = SplitText(sourceText, new[] { ' ' }, 3);

            var regex = new Regex($@"({baseRegex})|((.(?!{baseRegex}))+.)");

            // Конкурентная коллекция для хранения набора <индекс_блока,индекс_соответствия,хэш_код,шифр_блок>
            var fragments = new ConcurrentBag<Tuple<long, long, string, string>>();
            Parallel.ForEach(textBlocks, (block, blockState, i_b) =>
            {
                var matches = regex.Matches(block).OfType<Match>().ToList();
                Parallel.ForEach(matches, (match, matchState, i_m) =>
                {
                    if (Regex.IsMatch(match.Value, baseRegex))
                    {
                        fragments.Add(Tuple.Create(i_b, i_m, "хэш_код", "шифр_блок"));
                    }
                    else
                    {
                        fragments.Add(Tuple.Create(i_b, i_m, String.Empty, match.Value));
                    }
                });
            });

            var orderedFragments = fragments
                .AsParallel()
                .OrderBy(f => f.Item1)
                .ThenBy(f => f.Item2)
                .Select(f => Tuple.Create(f.Item1, f.Item2, f.Item3, f.Item4))
                .ToList();

            foreach (var fragment in orderedFragments)
            {
                Console.WriteLine(fragment.Item4);
            }

            encryptor.Dispose();
            decryptor.Dispose();
        }

        static byte[] EncryptMatch(string match, ICryptoTransform encryptor)
        {
            byte[] data = Encoding.UTF8.GetBytes(match);
            byte[] encryptedData;
            using (MemoryStream ms = new MemoryStream())
            {
                using (Stream c = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    c.Write(data, 0, data.Length);
                }
                encryptedData = ms.ToArray();
            }
            return encryptedData;
        }

        static string DecryptMatch(byte[] match, ICryptoTransform decryptor)
        {
            using (MemoryStream msInput = new MemoryStream(match))
            using (CryptoStream cryptoStream = new CryptoStream(msInput, decryptor, CryptoStreamMode.Read))
            using (MemoryStream msOutput = new MemoryStream())
            {
                cryptoStream.CopyTo(msOutput); // Копируем расшифрованные данные в msOutput
                return Encoding.UTF8.GetString(msOutput.ToArray()); // Преобразуем в строку
            }
        }

        //static string ComputeHash(string input)
        //{
        //    using (var sha256 = SHA256.Create())
        //    {
        //        var bytes = Encoding.UTF8.GetBytes(input);
        //        var hash = sha256.ComputeHash(bytes);
        //        return Convert.ToBase64String(hash);
        //    }
        //}

        //static void DecryptText(string filePath, string sqlitePath)
        //{
        //    // Чтение файла и таблицы SQLite, замена хеш-кодов на исходные данные
        //    // ...
        //}

        // Метод разбиения текста на блоки
        static List<string> SplitText(string text, char[] delimiters, int maxBlockSize)
        {
            var blocks = new List<string>();
            var currentBlock = new StringBuilder();

            foreach (var character in text)
            {
                currentBlock.Append(character);

                // Проверяем, является ли текущий символ одним из разделителей и достигнут ли максимальный размер блока
                if (delimiters.Contains(character) && currentBlock.Length >= maxBlockSize)
                {
                    blocks.Add(currentBlock.ToString());
                    currentBlock.Clear();
                }
            }

            // Добавляем оставшийся текст в список, если он не пустой
            if (currentBlock.Length > 0)
            {
                blocks.Add(currentBlock.ToString());
            }

            return blocks;
        }
    }
}
