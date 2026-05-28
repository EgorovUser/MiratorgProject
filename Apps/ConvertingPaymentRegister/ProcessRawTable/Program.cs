using CommonLib;
using CsvParsing;
using ProcessRawTable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PaymentCsvLib;

namespace ProcessRawTable
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 6)
                throw new ArgumentException(
                    "Ожидаются аргументы: [0] путь к csv-файлу, " +
                    "[1] путь до папки выхода (или пусто — перезапись), " +
                    "[2] разделитель, [3] код кредитора, " +
                    "[4] номер взаимозачёта, [5] год из SAP, " +
                    "[6] сумма из SAP (опционально).");

            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.InputFilePath, args[0] },
                { Parameters.OutputDirPath, args[1] },
                { Parameters.Delimiter, args[2] },
                { Parameters.CreditorCode, args[3] },
                { Parameters.OffsetNumber, args[4] },
                { Parameters.SapYear, args[5] }
            };

            if (args.Length > 6)
                parameters[Parameters.SapSum] = args[6];

            var result = Execute(parameters);
            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }

        // =====================================================================
        //  Основной метод
        // =====================================================================

        /// <summary>
        /// Основной метод обработки CSV-реестра кредитора.
        /// 
        /// Алгоритм:
        /// 1. Определяем конфигурацию кредитора по SAP-коду.
        /// 2. Парсим CSV-файл, извлекаем документы и суммы.
        /// 3. Формируем выходную таблицу (CsvTable) со стандартизированными столбцами.
        /// 4. Записываем результат в выходной файл.
        /// 5. Возвращаем JSON-результат со статистикой.
        /// </summary>
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            var result = new Dictionary<string, object>();

            // -----------------------------------------------------------------
            //  1. Извлечение параметров
            // -----------------------------------------------------------------
            string inputFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputFilePath);
            string outputDirPath = DictProcessor.ExtractOptionalString(parameters, Parameters.OutputDirPath);
            // Если outputDirPath — пустая строка, считаем что перезапись
            if (outputDirPath != null && string.IsNullOrWhiteSpace(outputDirPath))
                outputDirPath = null;
            char delimiter = DictProcessor.ExtractDelimiter(parameters, null);
            string creditorCode = DictProcessor.ExtractRequiredString(parameters, Parameters.CreditorCode);
            string offsetNumber = DictProcessor.ExtractRequiredString(parameters, Parameters.OffsetNumber);
            string sapYear = DictProcessor.ExtractRequiredString(parameters, Parameters.SapYear);
            string sapSumStr = DictProcessor.ExtractOptionalString(parameters, Parameters.SapSum);

            decimal sapSum = 0;
            if (!string.IsNullOrWhiteSpace(sapSumStr))
            {
                if (!DateHelper.TryParseDecimal(sapSumStr, out sapSum))
                    sapSum = 0;
            }

            // -----------------------------------------------------------------
            //  2. Поиск конфигурации кредитора
            // -----------------------------------------------------------------
            var config = CreditorMappingRegistry.FindByCode(creditorCode);
            if (config == null)
            {
                result["Status"] = "Error";
                result["ErrorMessage"] = $"Неизвестный код кредитора: {creditorCode}";
                return result;
            }

            result["CreditorCode"] = creditorCode;
            result["CreditorName"] = config.CreditorName;

            // -----------------------------------------------------------------
            //  3. Парсинг CSV-файла
            // -----------------------------------------------------------------
            var parser = new CsvRegistryParser();
            var parseResult = parser.Parse(inputFilePath, delimiter, config);

            if (!parseResult.Success)
            {
                result["Status"] = "Error";
                result["ErrorMessage"] = parseResult.ErrorMessage;
                return result;
            }

            // -----------------------------------------------------------------
            //  4. Формирование выходной таблицы
            // -----------------------------------------------------------------
            // Столбцы выходного файла:
            //   0 — Код кредитора
            //   1 — Наименование кредитора
            //   2 — Номер взаимозачёта
            //   3 — Год SAP
            //   4 — Сумма SAP
            //   5 — Номер документа
            //   6 — Дата документа
            //   7 — Сумма по документу (сумма зачёта)
            //   8 — Сумма по документу (дублирует 7 для совместимости)
            var headers = new List<string>
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

            var outputTable = new CsvTable(headers);

            string sapSumFormatted = sapSum != 0
                ? DateHelper.ToStringDecimal(sapSum, 2)
                : "";

            foreach (var row in parseResult.Rows)
            {
                var cells = new List<string>
                {
                    "1440",
                    creditorCode,
                    offsetNumber,
                    sapYear,
                    sapSumFormatted,
                    row.DocNumber ?? "",
                    row.DocDate ?? "",
                    DateHelper.ToStringDecimal(row.Sum, 2),
                    DateHelper.ToStringDecimal(row.Sum, 2)
                };

                outputTable.AddRow(cells);
            }

            // -----------------------------------------------------------------
            //  5. Запись выходного файла
            // -----------------------------------------------------------------
            string outputFilePath;
            string inputFileName = Path.GetFileName(inputFilePath);

            if (!string.IsNullOrWhiteSpace(outputDirPath))
            {
                // Запись в указанную папку
                if (!Directory.Exists(outputDirPath))
                    Directory.CreateDirectory(outputDirPath);

                outputFilePath = Path.Combine(outputDirPath, inputFileName);
            }
            else
            {
                // Перезапись исходного файла
                outputFilePath = inputFilePath;
            }

            var engine = new CsvProcessorEngine();
            engine.WriteCsv(outputTable, outputFilePath, delimiter);

            // -----------------------------------------------------------------
            //  6. Формирование результата
            // -----------------------------------------------------------------
            var docList = parseResult.Rows.Select(r => new Dictionary<string, object>
            {
                { "DocNumber", r.DocNumber ?? "" },
                { "DocDate", r.DocDate ?? "" },
                { "Sum", DateHelper.ToStringDecimal(r.Sum, 2) }
            }).ToList();

            result["Status"] = "OK";
            result["TotalRows"] = parseResult.TotalRows;
            result["SkippedRows"] = parseResult.SkippedRows;
            result["TotalSum"] = DateHelper.ToStringDecimal(parseResult.TotalSum, 2);
            result["OutputFilePath"] = outputFilePath;
            result["Documents"] = docList;

            // Сравнение с суммой из SAP (если передана)
            if (sapSum != 0)
            {
                decimal diff = sapSum - parseResult.TotalSum;
                result["SapSum"] = DateHelper.ToStringDecimal(sapSum, 2);
                result["Difference"] = DateHelper.ToStringDecimal(diff, 2);
                result["Match"] = Math.Abs(diff) < 0.01m;
            }

            return result;
        }
    }
}
