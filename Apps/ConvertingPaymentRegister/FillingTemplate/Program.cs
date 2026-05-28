using System;
using System.Collections.Generic;
using System.IO;
using CommonLib;
using CsvParsing;
using PaymentCsvLib;

namespace FillingTemplate
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

        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            // ---- 1. Извлекаем параметры ----

            string filePath = DictProcessor.ExtractRequiredString(parameters, Parameters.InputFilePath);
            string outputDirPath = DictProcessor.ExtractOptionalString(parameters, Parameters.OutputDirPath);
            char delimiter = DictProcessor.ExtractDelimiter(parameters, Parameters.Delimiter);
            string creditorCode = DictProcessor.ExtractRequiredString(parameters, Parameters.CreditorCode);
            string offsetNumber = DictProcessor.ExtractRequiredString(parameters, Parameters.OffsetNumber);
            string sapYear = DictProcessor.ExtractOptionalString(parameters, Parameters.SapYear);
            string sapSum = DictProcessor.ExtractOptionalString(parameters, Parameters.SapSum);

            // Формируем выходной путь
            string outputFilePath = string.IsNullOrWhiteSpace(outputDirPath)
                ? filePath
                : Path.Combine(outputDirPath, Path.GetFileName(filePath));

            // ---- 2. Читаем исходный CSV ----

            var engine = new CsvProcessorEngine();
            CsvTable sourceTable = engine.ReadCsv(filePath, delimiter);

            // ---- 3. Определяем конфигурацию кредитора ----

            var config = CreditorMappingRegistry.FindByCode(creditorCode);
            bool useContextFallback = config == null;

            if (useContextFallback)
            {
                config = new CreditorConfig
                {
                    CreditorName = "Неизвестный(" + creditorCode + ")"
                };
            }

            // ---- 4. Нет данных → возвращаем пустой шаблон ----

            if (sourceTable.Rows.Count == 0)
            {
                var emptyTable = TemplateBuilder.CreateEmpty();
                engine.WriteCsv(emptyTable, outputFilePath, delimiter);

                return Result(outputFilePath, 0, config.CreditorName,
                    "Исходный файл не содержит строк данных.");
            }

            // ---- 5. Спецслучай: реестр без расшифровки (Союз св.Иоанна) ----

            if (config.NoRegistryDetails)
            {
                var headerOnlyTable = TemplateBuilder.CreateHeaderOnly(
                    creditorCode, offsetNumber, sapYear, sapSum);
                engine.WriteCsv(headerOnlyTable, outputFilePath, delimiter);

                return Result(outputFilePath, 1, config.CreditorName,
                    "Контрагент без расшифровки — заполнена только шапка. " +
                    "Далее применяется алгоритм п.7.");
            }

            // ---- 6. Разрешаем индексы столбцов исходного реестра ----

            var columns = ColumnResolver.Resolve(sourceTable, config, useContextFallback);

            // ---- 7. Заполняем строки шаблона из реестра ----

            var outputTable = TemplateBuilder.CreateEmpty();
            int rowsProcessed = 0;

            foreach (var sourceRow in sourceTable.Rows)
            {
                // Пропускаем исключаемые строки
                if (RowFilter.ShouldExclude(sourceRow, config, columns.FilterColIdx))
                    continue;

                // --- ОБЩСУММА (столбец 8) ---
                string sumValue = "";
                if (columns.SumColIdx >= 0 && columns.SumColIdx < sourceRow.CellCount)
                    sumValue = (sourceRow[columns.SumColIdx] ?? "").Trim();

                // Пропускаем пустые суммы
                if (string.IsNullOrWhiteSpace(sumValue))
                    continue;

                // --- НОМЕР ДОКУМЕНТА (столбец 5) ---
                string docNumber = "";

                if (config.UseSapForDocNumber)
                {
                    // Дикси: номер документа через SAP ZSSD_120_VBRP (заглушка)
                    docNumber = ""; // TODO: SAP lookup
                }
                else if (columns.DocNumColIdx >= 0 && columns.DocNumColIdx < sourceRow.CellCount)
                {
                    docNumber = (sourceRow[columns.DocNumColIdx] ?? "").Trim();

                    // Fallback для Окей: если пуст «№ счета-фактуры» — берём «№ накладной»
                    if (string.IsNullOrWhiteSpace(docNumber)
                        && columns.DocNumFallbackIdx >= 0
                        && columns.DocNumFallbackIdx < sourceRow.CellCount)
                    {
                        docNumber = (sourceRow[columns.DocNumFallbackIdx] ?? "").Trim();
                    }
                }

                // Для Виктория Балтия: извлечь только номер (часть до даты)
                if (config.ParseDocNumberBeforeDate && !string.IsNullOrWhiteSpace(docNumber))
                {
                    docNumber = DocNumberExtractor.ExtractBeforeDate(docNumber);
                }

                // --- Формируем строку шаблона ---
                var outputCells = TemplateBuilder.BuildRow(
                    creditorCode, offsetNumber,
                    sapYear, sapSum,
                    docNumber,
                    sumValue);

                outputTable.AddRow(outputCells);
                rowsProcessed++;
            }

            // ---- 8. Если после фильтрации не осталось строк ----

            if (rowsProcessed == 0)
            {
                var emptyTable = TemplateBuilder.CreateEmpty();
                engine.WriteCsv(emptyTable, outputFilePath, delimiter);

                return Result(outputFilePath, 0, config.CreditorName,
                    "После фильтрации не осталось строк для обработки.");
            }

            // ---- 9. Нормализуем числа в ОБЩСУММА ----
            outputTable.NormalizeNumbers(8);

            // ---- 10. Записываем выходной файл ----
            engine.WriteCsv(outputTable, outputFilePath, delimiter);

            return Result(outputFilePath, rowsProcessed, config.CreditorName);
        }

        // =====================================================================
        //  Вспомогательный метод формирования результата
        // =====================================================================

        private static IDictionary<string, object> Result(
            string outputFilePath,
            int rowsProcessed,
            string creditorName,
            string message = null)
        {
            var dict = new Dictionary<string, object>
            {
                { "outputFilePath", outputFilePath },
                { "rowsProcessed", rowsProcessed },
                { "creditor", creditorName }
            };

            if (message != null)
                dict["message"] = message;

            return dict;
        }
    }
}
