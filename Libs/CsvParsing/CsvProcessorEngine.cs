using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.IO;

namespace CsvParsing
{
    /// <summary>
    /// Движок чтения и записи CSV-файлов.
    /// Поддерживает кавычки, экранирование и различные разделители.
    /// </summary>
    public class CsvProcessorEngine
    {
        public CsvProcessorEngine() { }

        /// <summary>
        /// Читает CSV-файл и возвращает объект CsvTable.
        /// </summary>
        /// <param name="filePath">Путь к CSV-файлу.</param>
        /// <param name="delimiter">Разделитель столбцов.</param>
        /// <param name="hasHeaders">
        /// true — первая строка содержит заголовки (по умолчанию).
        /// false — первая строка содержит данные; заголовки генерируются автоматически (Column1, Column2, ...).
        /// </param>
        /// <returns>Заполненную таблицу CsvTable.</returns>
        public CsvTable ReadCsv(string filePath, char delimiter)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь к CSV-файлу не указан.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV-файл не найден: {filePath}", filePath);

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length == 0)
                throw new InvalidDataException($"CSV-файл пуст: {filePath}");

            List<string> headers;

            headers = ParseCsvLine(lines[0], delimiter);
            if (headers.Count == 0)
                throw new InvalidDataException($"Заголовки не найдены в первой строке файла: {filePath}");

            var table = new CsvTable(headers);

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var cells = ParseCsvLine(line, delimiter);

                if (cells.Count < headers.Count)
                {
                    while (cells.Count < headers.Count)
                        cells.Add("");
                }

                if (cells.Count > headers.Count)
                    continue;

                table.AddRow(cells);
            }

            return table;
        }

        /// <summary>
        /// Записывает таблицу CsvTable в CSV-файл.
        /// </summary>
        /// <param name="table">Таблица для записи.</param>
        /// <param name="filePath">Путь к выходному файлу.</param>
        /// <param name="delimiter">Разделитель столбцов.</param>
        /// <param name="lineSeparator">Разделитель строк. По умолчанию \r\n.</param>
        public void WriteCsv(CsvTable table, string filePath, char delimiter, string lineSeparator = "\r\n")
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table), "Таблица не может быть null.");
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь к выходному файлу не указан.", nameof(filePath));

            var content = table.ToCsvString(delimiter, lineSeparator);
            File.WriteAllText(filePath, content, Encoding.UTF8);
        }

        /// <summary>
        /// Разбирает одну строку CSV с учётом кавычек и экранирования.
        /// </summary>
        private List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == delimiter)
                    {
                        result.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }

            result.Add(current.ToString().Trim());
            return result;
        }

        private string ExtractString(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out object value))
                return value?.ToString();
            return null;
        }
    }
}
