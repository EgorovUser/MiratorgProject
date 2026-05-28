using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CsvParsing;
using CommonLib;

namespace FillCsvTableApp
{
    public static class Parameters
    {
        public const string InputFilePath = "inputFilePath";
        public const string Delimiter = "delimiter";
        public const string OutputDirPath = "outputDirPath";

        public const string NewDate = "newDate";
        public const string NewFile = "newFile";
    }
    internal class Program
    {
        /// <summary>
        /// Заголовки выходного CSV-файла.
        /// </summary>
        private static readonly string[] OutputHeaders =
        {
            "БЕ",
            "КОД ПОСТАВЩ",
            "НОМЕР ВЗ (72***)",
            "ГОД",
            "СУММА",
            "НОМЕР ДОКУМЕНТА",
            "ДАТА ДОКУМЕНТА",
            "ОБЩСУММА",
            "ОБЩСУММАОСТ"
        };
        /// <summary>
        /// Константа для столбца БЕ.
        /// </summary>
        private const string BeConstant = "1440";
        static void Main(string[] args)
        {
            if (args.Length < 3)
                throw new ArgumentException("Ожидаются 3 аргумента: [0] путь к csv-файлу, [1] путь до конечного csv файла [2] разделитель.");
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.InputFilePath, args[0] },
                { Parameters.OutputDirPath, args[1] },
                { Parameters.Delimiter, args[2] },
            };

            var result = Execute(parameters);

            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            // ---- 1. Извлекаем параметры ----

            string filePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputFilePath);
            string outputDirPath = DictProcessor.ExtractOptionalString(parameters, Parameters.OutputDirPath);
            char delimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);
            string outputFilePath = string.Empty;
            // Если путь к выходному файлу не задан — формируем автоматически
            if (string.IsNullOrWhiteSpace(outputDirPath))
            {
                outputFilePath = filePath;
            }
            else
            {
                outputFilePath = Path.Combine(outputDirPath, Path.GetFileName(filePath));
            }

            // ---- 2. Читаем исходный CSV ----

            var engine = new CsvProcessorEngine();
            var table = engine.ReadCsv(filePath, delimiter);

            if (table.Rows.Count == 0)
                throw new InvalidOperationException(
                    $"Исходный CSV-файл не содержит строк с данными: {filePath}");

            // ---- 3. Формируем выходной CSV ----

            var sb = new StringBuilder();

            // Заголовки
            sb.Append(string.Join(delimiter.ToString(), OutputHeaders));
            sb.Append("\r\n");

            int rowsProcessed = 0;

            foreach (var row in table.Rows)
            {
                // Проверяем, что SAP-код (столбец 1, индекс 1) корректный
                string sapCode = row[1]?.Trim();
                if (string.IsNullOrWhiteSpace(sapCode))
                    continue;

                // Проверяем, что номер взаимозачёта (столбец 2, индекс 2) корректный
                string settlementNumber = row[2]?.Trim();

                // Строим строку: БЕ;КОД ПОСТАВЩ;НОМЕР ВЗ (72***);ГОД;СУММА;НОМЕР ДОКУМЕНТА;ДАТА ДОКУМЕНТА;ОБЩСУММА;ОБЩСУММАОСТ
                var cells = new string[]
                {
                    BeConstant,             // БЕ = 1440
                    sapCode,                // КОД ПОСТАВЩ
                    settlementNumber ?? "", // НОМЕР ВЗ (72***)
                    "",                     // ГОД — пусто
                    "",                     // СУММА — пусто
                    "",                     // НОМЕР ДОКУМЕНТА — пусто
                    "",                     // ДАТА ДОКУМЕНТА — пусто
                    "",                     // ОБЩСУММА — пусто
                    ""                      // ОБЩСУММАОСТ — пусто
                };

                sb.Append(string.Join(delimiter.ToString(), cells));
                sb.Append("\r\n");
                rowsProcessed++;
            }

            if (rowsProcessed == 0)
                throw new InvalidOperationException(
                    "Не удалось обработать ни одной строки: все строки имеют пустой SAP-код.");

            // ---- 4. Записываем выходной файл ----

            File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);

            /*
            // ---- 5. Возвращаем результат ----
            DateTime now = DateTime.Now;
            DateTime resultDate = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
            string formatted = resultDate.ToString("dd.MM.yyyy");*/

            var result = new Dictionary<string, object>
            {
                { Parameters.NewFile, outputFilePath }
            };

            return result;
        }
    }
}
