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
using Stego;


namespace ParallelTextMatchCipherConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            const string baseRegex = @"\d+"; // Регулярное выражение, задаваемое пользователем 
            const string privateTextPath = "PrivateText.json";
            const int n = 36;
            const int blockSize = 7;

            string sourceText = @"Гарри Кимович Каспаров (фамилия при рождении Вайнштейн; род. 13 апреля 1963, 
Баку, Азербайджанская ССР, СССР) — советский и российский шахматист,
13 - й чемпион мира по шахматам, шахматный литератор и политик, часто
признаваемый величайшим шахматистом
.";

            var separators = new[] { ' ' };
            var textBlocks = SplitText(sourceText, separators, blockSize);
            var regex = new Regex($@"({baseRegex})|((.(?!{baseRegex}))*.)", RegexOptions.Singleline);

            var stegoMask = new Stegomask(n);
            var stegoAlg = new StegoAlg(n);
            var fragments = new ConcurrentBag<CipherDataSet>(); // Конкурентная коллекция для хранения набора <флаг_приватности,индекс_блока,индекс_соответствия,текстовый_блок>

            Parallel.ForEach(textBlocks, (block, blockState, i_b) =>
            {
                var matches = regex.Matches(block).OfType<Match>().ToList();
                Parallel.ForEach(matches, (match, matchState, i_m) =>
                {
                    if (Regex.IsMatch(match.Value, baseRegex))
                    {
                        fragments.Add(new CipherDataSet
                        {
                            IsPrivateText = true,
                            Id = new IndexData { block = i_b, match = i_m }.EncryptID(stegoAlg),
                            Text = Encrypt(match.Value, stegoAlg)
                        });
                    }
                    else
                    {
                        fragments.Add(new CipherDataSet
                        {
                            IsPrivateText = false,
                            Id = new IndexData { block = i_b, match = i_m }.EncryptID(stegoAlg),
                            Text = match.Value
                        });
                    }
                });
            });

            SerializeToJson(fragments, privateTextPath);

            var fragments2 = DeserializeFromJson(privateTextPath);

            var orderedFragments = fragments2
                .AsParallel()
                .OrderBy(ds =>
                {
                    var id = new IndexData();
                    id.DecryptID(ds.Id, stegoAlg);
                    return (id.block, id.match);
                })
                .Select(ds => ds.IsPrivateText ? Decrypt(ds.Text, stegoAlg) : ds.Text)
                .ToList();

            foreach (var fragment in orderedFragments)
            {
                Console.Write(fragment);
            }    

            stegoAlg.Dispose();
        }

        [DataContract]
        public struct CipherDataSet
        {
            [DataMember]
            public bool IsPrivateText;
            [DataMember]
            public string Id;
            [DataMember]
            public string Text;
        }

        [DataContract]
        public struct IndexData
        {
            [DataMember(Order = 1)]
            public long match;
            [DataMember(Order = 2)]
            public long block;

            public string EncryptID(StegoAlg stegoAlg)
            {
                var serializer = new DataContractJsonSerializer(typeof(IndexData));
                var ms = new MemoryStream();
                serializer.WriteObject(ms, this);
                string data = Encoding.UTF8.GetString(ms.ToArray());

                return Encrypt(data, stegoAlg);
            }

            public void DecryptID(string encryptedData, StegoAlg stegoAlg)
            {
                string json = Decrypt(encryptedData, stegoAlg);

                var serializer = new DataContractJsonSerializer(typeof(IndexData));
                var data = (IndexData)serializer.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(json)));

                this.block = data.block;
                this.match = data.match;
            }
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

        static string Encrypt(string text, StegoAlg stegoAlg)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            using (var ms = new MemoryStream())
            {
                using (Stream c = new StegoStream(ms, stegoAlg))
                {
                    c.Write(data, 0, data.Length);
                }

                var encryptedData = ms.ToArray();
                return Convert.ToBase64String(encryptedData);
            }
        }

        static string Decrypt(string text, StegoAlg stegoAlg)
        {
            using (var msInput = new MemoryStream(Convert.FromBase64String(text)))
            using (var stegoStream = new StegoStream(msInput, stegoAlg))
            using (var msOutput = new MemoryStream())
            {
                stegoStream.CopyTo(msOutput);
                return Encoding.UTF8.GetString(msOutput.ToArray()); 
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
