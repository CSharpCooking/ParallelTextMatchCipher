using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;


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

            // Разделитель для разбивки текста на блоки
            char[] separators = new[] { '_' };

            // Минимальный размер блока
            const int blockSize = 3;

            SymmetricAlgorithm algorithm = Aes.Create();
            byte[] iv = { 15, 122, 132, 5, 93, 198, 44, 31, 9, 39, 241, 49, 250, 188, 80, 7 };
            byte[] key;
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                key = sha256.ComputeHash(passwordBytes);
            }

            var encryptor = new ThreadLocal<ICryptoTransform>(() => algorithm.CreateEncryptor(key, iv), trackAllValues: true);
            var decryptor = new ThreadLocal<ICryptoTransform>(() => algorithm.CreateDecryptor(key, iv), trackAllValues: true);

            // Пример исходного текста
            string sourceText = @"Га́рри Ки́мович Каспа́ров (фамилия при рождении Вайнште́йн; род. 13 апреля 1963, 
Баку, Азербайджанская ССР, СССР) — советский и российский шахматист,
13 - й чемпион мира по шахматам, шахматный литератор и политик, часто
признаваемый величайшим шахматистом
.";

            // Разбиваем текст на блоки
            var textBlocks = SplitText(sourceText, separators, blockSize);

            var regex = new Regex($@"({baseRegex})|((.(?!{baseRegex}))*.)",RegexOptions.Singleline);

            // Конкурентная коллекция для хранения набора <флаг_приватности,индекс_блока,индекс_соответствия,текстовый_блок>
            var fragments = new ConcurrentBag<CipherDataSet>();
            Parallel.ForEach(textBlocks, (block, blockState, i_b) =>
            {
                var matches = regex.Matches(block).OfType<Match>().ToList();
                Parallel.ForEach(matches, (match, matchState, i_m) =>
                {
                    if (Regex.IsMatch(match.Value, baseRegex))
                        fragments.Add(new CipherDataSet
                        {
                            privateText = true,
                            indexBlock = Encrypt(i_b.ToString(), encryptor),
                            indexMatch = Encrypt(i_m.ToString(), encryptor),
                            text = Encrypt(match.Value, encryptor)
                        });
                    else
                        fragments.Add(new CipherDataSet
                        {
                            privateText = false,
                            indexBlock = Encrypt(i_b.ToString(), encryptor),
                            indexMatch = Encrypt(i_m.ToString(), encryptor),
                            text = match.Value
                        });
                });
            });

            SerializeToJson(fragments, "c:\\Users\\landw\\Downloads\\PrivateText.json");

            var fragments2 = DeserializeFromJson("c:\\Users\\landw\\Downloads\\PrivateText.json");

            var orderedFragments = fragments2
                .AsParallel()
                .OrderBy(ds => Int64.Parse(Decrypt(ds.indexBlock, decryptor)))
                .ThenBy(ds => Int64.Parse(Decrypt(ds.indexMatch, decryptor)))
                .Select(ds => ds.privateText ? Decrypt(ds.text, decryptor) : ds.text)
                .ToList();

            foreach (var fragment in orderedFragments)
                Console.Write(fragment);

            // Освобождение ресурсов ICryptoTransform в каждом потоке
            foreach (var enc in encryptor.Values)
            {
                enc.Dispose();
            }

            foreach (var dec in decryptor.Values)
            {
                dec.Dispose();
            }

            // Освобождение ресурсов ThreadLocal<ICryptoTransform>
            encryptor.Dispose();
            decryptor.Dispose();
        }

        [DataContract]
        public struct CipherDataSet
        {
            [DataMember]
            public bool privateText;
            [DataMember]
            public string indexBlock;
            [DataMember]
            public string indexMatch;
            [DataMember]
            public string text;
        }

        static void SerializeToJson(ConcurrentBag<CipherDataSet> fragments, string filePath)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ConcurrentBag<CipherDataSet>));
                using (var fileStream = File.Create(filePath))
                {
                    serializer.WriteObject(fileStream, fragments);
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        static IEnumerable<CipherDataSet> DeserializeFromJson(string filePath)
        {
            var serializer = new DataContractJsonSerializer(typeof(IEnumerable<CipherDataSet>));
            using (var fileStream = File.OpenRead(filePath))
            {
                return (IEnumerable<CipherDataSet>)serializer.ReadObject(fileStream);
            }
        }

        static string Encrypt(string text, ThreadLocal<ICryptoTransform> encryptor)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] encryptedData;
            using (MemoryStream ms = new MemoryStream())
            {
                using (Stream c = new CryptoStream(ms, encryptor.Value, CryptoStreamMode.Write))
                {
                    c.Write(data, 0, data.Length);
                }
                encryptedData = ms.ToArray();
            }
            return Convert.ToBase64String(encryptedData);
        }

        static string Decrypt(string text, ThreadLocal<ICryptoTransform> decryptor)
        {
            using (MemoryStream msInput = new MemoryStream(Convert.FromBase64String(text)))
            using (CryptoStream cryptoStream = new CryptoStream(msInput, decryptor.Value, CryptoStreamMode.Read))
            using (MemoryStream msOutput = new MemoryStream())
            {
                cryptoStream.CopyTo(msOutput); // Копируем расшифрованные данные в msOutput
                return Encoding.UTF8.GetString(msOutput.ToArray()); // Преобразуем в строку
            }
        }

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
