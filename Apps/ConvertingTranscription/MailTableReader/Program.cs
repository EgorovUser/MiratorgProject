using System;
using System.IO;
using System.Linq;
using System.Text;
using HtmlAgilityPack;
using Aspose.Email.Mapi;
using System.Net;

namespace MailTableReader
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 3)
                throw new ArgumentException("Ожидаются 3 аргумента: путь к .msg файлу, путь к конечной папке и разделитель");

            string msgPath = args[0];
            string csvFolder = args[1];
            string delimiter = args[2];

            if (!Directory.Exists(csvFolder))
                Directory.CreateDirectory(csvFolder);

            string csvPath = Path.Combine(csvFolder, Path.GetFileNameWithoutExtension(msgPath) + ".csv");

            // Читаем MSG напрямую, без Outlook
            MapiMessage mail = MapiMessage.Load(msgPath);

            string html = mail.BodyHtml;
            if (string.IsNullOrWhiteSpace(html))
                throw new Exception("В сообщении нет HTML-тела");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tables = doc.DocumentNode.SelectNodes("//table");
            if (tables == null)
                throw new Exception("Таблицы не найдены");

            HtmlNode targetTable = null;

            foreach (var table in tables)
            {
                var firstTr = table.SelectSingleNode(".//tr");
                if (firstTr == null)
                    continue;

                string firstTrText = Normalize(firstTr.InnerText);

                if (firstTrText.Contains("Сведения о переводе"))
                {
                    targetTable = table;
                    break;
                }
            }

            if (targetTable == null)
                throw new Exception("Нужная таблица не найдена");

            var rows = targetTable.SelectNodes(".//tr");
            if (rows == null || rows.Count <= 2)
                throw new Exception("Недостаточно строк");

            using (var writer = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
            {
                foreach (var row in rows.Skip(1).Take(rows.Count - 2))
                {
                    var cells = row.SelectNodes("./th|./td");
                    if (cells == null) continue;

                    string[] values = cells
                        .Select(c => CleanText(c.InnerText))
                        .ToArray();

                    writer.WriteLine(string.Join(delimiter, values.Select(EscapeDelimited)));
                }
            }

            Console.WriteLine(csvPath);
        }

        static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return string.Join(" ",
                WebUtility.HtmlDecode(text).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();
        }

        static string CleanText(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            s = HtmlEntity.DeEntitize(s);
            s = WebUtility.HtmlDecode(s);

            s = s.Replace("\u00A0", " ");
            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            s = string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            return s.Trim();
        }

        static string EscapeDelimited(string value)
        {
            if (value == null)
                return string.Empty;

            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }
    }
}