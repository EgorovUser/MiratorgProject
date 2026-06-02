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
        public const string DebtorCode = "debtorCode";
        public const string VZCode = "vzCode";
        public const string OutputDirPath = "outputDirPath";
        public const string Delimiter = "delimiter";

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
                { Parameters.DebtorCode, args[0] },
                { Parameters.VZCode, args[1] },
                { Parameters.OutputDirPath, args[2] },
                { Parameters.Delimiter, args[2] },
            };

            var result = Execute(parameters);

            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            // ---- 1. Извлекаем параметры ----

            string debtorCode = DictProcessor.ExtractRequiredString(parameters, Parameters.DebtorCode);
            string vzCode = DictProcessor.ExtractOptionalString(parameters, Parameters.VZCode);
            string outputDirPath = DictProcessor.ExtractOptionalString(parameters, Parameters.OutputDirPath);
            char delimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);

            string outputFilePath = string.Empty;
            // Если путь к выходному файлу не задан — формируем автоматически
            DateTime now = DateTime.Now;
            string formatted = now.ToString("dd.MM.yyyy_HH-mm-ss");
            outputFilePath = Path.Combine(outputDirPath, formatted + ".csv");

            // ---- 2. Формируем выходной CSV ----

            var sb = new StringBuilder();

            // Заголовки
            sb.Append(string.Join(delimiter.ToString(), OutputHeaders));
            sb.Append("\r\n");

            // Строим строку: БЕ;КОД ПОСТАВЩ;НОМЕР ВЗ (72***);ГОД;СУММА;НОМЕР ДОКУМЕНТА;ДАТА ДОКУМЕНТА;ОБЩСУММА;ОБЩСУММАОСТ
            var cells = new string[]
            {
                BeConstant,             // БЕ = 1440
                debtorCode,             // КОД ПОСТАВЩ
                vzCode ?? "",           // НОМЕР ВЗ (72***)
                "",                     // ГОД — пусто
                "",                     // СУММА — пусто
                "",                     // НОМЕР ДОКУМЕНТА — пусто
                "",                     // ДАТА ДОКУМЕНТА — пусто
                "",                     // ОБЩСУММА — пусто
                ""                      // ОБЩСУММАОСТ — пусто
            };

            sb.Append(string.Join(delimiter.ToString(), cells));
            sb.Append("\r\n");

            // ---- 3. Записываем выходной файл ----

            File.WriteAllText(outputFilePath, sb.ToString(), Encoding.UTF8);

            
            // ---- 4. Возвращаем результат ----
            DateTime resultDate = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
            string formattedDate = resultDate.ToString("dd.MM.yyyy");

            var result = new Dictionary<string, object>
            {
                { Parameters.NewFile, outputFilePath },
                { Parameters.NewDate, formattedDate }
            };

            return result;
        }
    }
}
