using CommonLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using TxtTableParsing;

namespace FBL5H_ParsingApp
{
    /// <summary>Имена параметров, передаваемых через IDictionary</summary>
    public static class Parameters
    {
        public const string ImportPath = "importPath";
        public const string ExportPath = "exportFolder";
        public const string FilePath = "filePath";
    }

    /// <summary>
    /// Конфигурация парсера SAP ALV (txt с табуляторами).
    /// </summary>
    public static class SapAlvConfig
    {
        /// <remarks>
        /// Имена столбцов соответствуют заголовкам из SAP-выгрузки
        /// «Текст с табуляторами» (Report → List → Export → Spreadsheet).
        /// </remarks>
        public static class Columns
        {
            public const string Debtor = "Дебитор";
            public const string DocumentDate = "ДатаДокум";
            public const string DebitCreditIndicator = "Д/К";
            public const string Currency = "Влт";
            public const string DocumentType = "Вид";
            public const string DocumentNumber = "№ докум.";
        }

        // ─── Обязательные столбцы для поиска заголовка ──────────────────
        public static readonly string[] RequiredHeaderColumns =
        {
            Columns.Debtor,
            Columns.DebitCreditIndicator,
            Columns.Currency
        };

        // ─── Фильтры ────────────────────────────────────────────────────
        public static class Filters
        {
            /// <summary>Допустимые значения столбца «Д/К»</summary>
            public static readonly HashSet<string> ValidIndicators =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "H", "S" };

            /// <summary>Типы документов для фильтрации строк H</summary>
            public static readonly List<string> RequiredDocumentTypes =
                new List<string> { "DC", "DZ", "AB", "KU", "VZ" };
        }


        // ─── Настройки парсера (передаются в TxtLib.TabDelimitedParser) ─
        public static ParserOptions CreateParserOptions()
        {
            return new ParserOptions
            {
                Delimiter = '\t',
                RequiredColumns = RequiredHeaderColumns,
                YearFieldIndex = 2,
                MaxHeaderSearchRows = 20
            };
        }
    }

    /// <summary>
    /// Финальная запись результата для одного дебитора + одной валюты.
    /// Одна запись = один дебитор + одна валюта + своя ранняя дата по H.
    /// </summary>
    [XmlRoot("DebtorResult")]
    public class DebtorResult
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlElement("debtor")]
        public string Debtor { get; set; } = string.Empty;

        [XmlElement("currency")]
        public string Currency { get; set; } = string.Empty;

        [XmlElement("earliestDate")]
        public string EarliestDate { get; set; } = string.Empty;

        [XmlElement("documentNumber")]
        public string DocumentNumber { get; set; } = string.Empty;
    }

    /// <summary>
    /// Обёртка над итоговым результатом парсинга.
    /// </summary>
    [XmlRoot("ParseOutput")]
    public class ParseOutput
    {
        [XmlArray("results")]
        [XmlArrayItem("DebtorResult")]
        public List<DebtorResult> Results { get; set; } = new List<DebtorResult>();
    }

    /// <summary>
    /// Точка входа приложения и оркестратор парсинга SAP ALV.
    /// Содержит Main (для локального запуска), Execute (для внешнего вызова)
    /// и Process (бизнес-логика фильтрации и группировки).
    /// </summary>
    
    internal class Program
    {
        static string debugString = "";
        static void Main(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException(
                    "Ожидаются 2 аргумента: [0] путь к txt-файлу, [1] папка для сохранения XML.");
            IDictionary<string, object> parameters = new Dictionary<string, object>()
            {
                { Parameters.ImportPath, args[0] },
                { Parameters.ExportPath, args[1] }
            };

            var result = Execute(parameters);
            Console.WriteLine(SerializationHelper.DictionaryToJson(result));
        }

        /// <summary>
        /// Основной метод обработки: парсинг → бизнес-логика → сохранение XML.
        /// </summary>
        /// <param name="parameters">
        ///   importPath — путь к txt-файлу
        ///   exportFolder — папка для сохранения результата
        /// </param>
        /// <returns>Словарь с ключом "filePath" = путь к сохранённому XML</returns>
        public static IDictionary<string, object> Execute(IDictionary<string, object> parameters)
        {
            // ─── Извлечение параметров ──────────────────────────────────
            string inputFilePath = DictProcessor.ExtractRequiredString(parameters, Parameters.ImportPath);
            string outputFolder = DictProcessor.ExtractRequiredString(parameters, Parameters.ExportPath);

            var resultDict = new Dictionary<string, object>
            {
                { Parameters.FilePath, string.Empty }
            };

            if (!Directory.Exists(outputFolder))
                throw new Exception($"Папка экспорта не найдена: {outputFolder}");

            if (!File.Exists(inputFilePath))
                throw new Exception($"Файл txt не найден: {inputFilePath}");

            DateTime now = DateTime.Now;
            string outputFileName = $"DebPos_{now:dd.MM.yyyy}_{now:HH}_{now:mm}_{now:ss}.xml";
            string outputPath = Path.Combine(outputFolder, outputFileName);

            try
            {
                // 1. Парсинг файла (TxtLib)
                var parser = new TabDelimitedParser(inputFilePath, SapAlvConfig.CreateParserOptions());
                ParsedTable table = parser.Parse();

                // 2. Бизнес-логика (фильтрация, группировка)
                ParseOutput output = Process(table);

                // 3. Сохранение XML (TxtLib)
                SerializationHelper.SaveXml(output, outputPath);

                resultDict[Parameters.FilePath] = outputPath;

                Debug.WriteLine($"[Execute] Сохранено {output.Results.Count} записей в {outputPath}");
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Ошибка при обработке: {ex.InnerException?.Message ?? ex.Message}\n{ex.StackTrace}");
            }

            return resultDict;
        }

        // ────────────────────────────────────────────────────────────────
        //  Process — бизнес-логика (уникальна для каждого источника)
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Фильтрует, группирует и формирует результат на основе спарсенной таблицы.
        ///
        /// Логика:
        ///   1. Фильтрация: Д/К ∈ {H, S} И дата ≠ пусто
        ///   2. Группировка по дебитору
        ///   3. Для каждого дебитора: проверка наличия H и S
        ///   4. Нахождение валют, общих для H и S
        ///   5. Для каждой валюты: фильтрация H-строк по типам {"DC", "DZ", "AB", "KU", "VZ"}
        ///   6. Выбор самой ранней даты → формирование DebtorResult
        /// </summary>
        public static ParseOutput Process(ParsedTable table)
        {
            if (table == null || !table.HasData)
                throw new InvalidOperationException("Нет данных для обработки.");

            // ─── Шаг 1: первичная фильтрация ────────────────────────────
            var filtered = table.Where(r =>
                SapAlvConfig.Filters.ValidIndicators.Contains(r[SapAlvConfig.Columns.DebitCreditIndicator]) &&
                r.HasValue(SapAlvConfig.Columns.DocumentDate));

            Debug.WriteLine($"[Process] После первичного фильтра: {filtered.Count} строк");

            // ─── Шаг 2: группировка по дебитору ────────────────────────
            var grouped = filtered
                .GroupBy(r => r[SapAlvConfig.Columns.Debtor], StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToList();

            var results = new List<DebtorResult>();
            int id = 0;

            // ─── Шаг 3: обработка каждого дебитора ─────────────────────
            foreach (var group in grouped)
            {
                string debtor = group.Key.Trim();
                var rows = group.ToList();

                // 3a. Проверяем наличие обоих индикаторов — H и S
                var indicators = new HashSet<string>(
                    rows.Select(r => r[SapAlvConfig.Columns.DebitCreditIndicator]),
                    StringComparer.OrdinalIgnoreCase);

                if (!indicators.Contains("H") || !indicators.Contains("S"))
                {
                    Debug.WriteLine($"[Process] Пропуск дебитора {debtor}: нет H или S");
                    continue;
                }

                // 3b. Валюты, у которых есть и H, и S
                var matchingCurrencies = GetCommonCurrencies(rows, SapAlvConfig.Columns.Currency);
                if (matchingCurrencies.Count == 0)
                {
                    Debug.WriteLine($"[Process] Пропуск дебитора {debtor}: нет совпадающих валют");
                    continue;
                }

                // 3c. Для каждой совпадающей валюты — отдельная запись
                foreach (var currency in matchingCurrencies)
                {
                    // H-строки с этой валютой
                    var rowsH = rows.Where(r =>
                        r.EqualsValue(SapAlvConfig.Columns.DebitCreditIndicator, "H") &&
                        r.EqualsValue(SapAlvConfig.Columns.Currency, currency)).ToList();

                    // Фильтрация по типам документов
                    var rowsFiltered = rowsH.Where(r =>
                        SapAlvConfig.Filters.RequiredDocumentTypes.Contains(
                            r[SapAlvConfig.Columns.DocumentType])).ToList();

                    // Самая ранняя дата
                    Row earliest = ParsedTable.GetEarliestRow(rowsFiltered, SapAlvConfig.Columns.DocumentDate);
                    if (earliest == null)
                    {
                        Debug.WriteLine($"[Process] Пропуск дебитора {debtor} ({currency}): нет дат");
                        continue;
                    }

                    results.Add(new DebtorResult
                    {
                        Id = id++,
                        Debtor = debtor,
                        Currency = currency,
                        EarliestDate = earliest[SapAlvConfig.Columns.DocumentDate],
                        DocumentNumber = earliest[SapAlvConfig.Columns.DocumentNumber]
                    });
                }
            }

            Debug.WriteLine($"[Process] Итог: {results.Count} записей (дебитор + валюта)");

            return new ParseOutput { Results = results };
        }

        // ────────────────────────────────────────────────────────────────
        //  Приватные вспомогательные методы
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Возвращает список валют, у которых есть хотя бы одна строка H
        /// И одна строка S у данного дебитора.
        /// </summary>
        private static List<string> GetCommonCurrencies(List<Row> rows, string currencyColumn)
        {
            var hCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                string currency = row[currencyColumn].Trim();
                if (string.IsNullOrWhiteSpace(currency))
                    continue;

                if (row.EqualsValue(SapAlvConfig.Columns.DebitCreditIndicator, "H"))
                    hCurrencies.Add(currency);
                else if (row.EqualsValue(SapAlvConfig.Columns.DebitCreditIndicator, "S"))
                    sCurrencies.Add(currency);
            }

            if (!hCurrencies.Overlaps(sCurrencies))
                return new List<string>();

            hCurrencies.IntersectWith(sCurrencies);
            return hCurrencies.ToList();
        }

    }
}
