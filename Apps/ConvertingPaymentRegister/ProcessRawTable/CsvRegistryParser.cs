using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CommonLib;
using PaymentCsvLib;

namespace ProcessRawTable
{
    // =====================================================================
    //  Результат парсинга одной строки реестра
    // =====================================================================

    /// <summary>
    /// Одна распарсенная строка реестра кредитора.
    /// </summary>
    public class ParsedRegistryRow
    {
        /// <summary>Номер документа (из реестра или SAP).</summary>
        public string DocNumber { get; set; }

        /// <summary>Дата документа в формате dd.MM.yyyy (или исходная строка).</summary>
        public string DocDate { get; set; }

        /// <summary>Сумма по документу.</summary>
        public decimal Sum { get; set; }

        /// <summary>Исходная строка CSV (для отладки).</summary>
        public string RawLine { get; set; }
    }

    // =====================================================================
    //  Общий результат парсинга
    // =====================================================================

    /// <summary>
    /// Результат парсинга реестра кредитора.
    /// </summary>
    public class ParseResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string CreditorCode { get; set; }
        public string CreditorName { get; set; }
        public List<ParsedRegistryRow> Rows { get; set; } = new List<ParsedRegistryRow>();
        public decimal TotalSum { get; set; }
        public int TotalRows { get; set; }
        public int SkippedRows { get; set; }
    }

    // =====================================================================
    //  Парсер CSV-реестров
    // =====================================================================

    /// <summary>
    /// Парсер CSV-реестров кредиторов.
    /// Поддерживает автоматический поиск заголовков, фильтрацию строк,
    /// извлечение сумм и номеров документов по конфигурации кредитора.
    /// </summary>
    public class CsvRegistryParser
    {
        private static readonly CultureInfo RuCulture = new CultureInfo("ru-RU");

        // Маркеры строк, которые следует пропускать (цифровые подписи, штампы, колонтитулы)
        private static readonly string[] StampMarkers =
        {
            "Передан через Диадо",
            "Передан через оператор",
            "Идентификатор документа",
            "Подписант",
            "Сертификат",
            "Дата и время подписания",
            "Доверенность",
            "Подпись соответствует",
            "Страница",
            "Документ подписан"
        };

        // Маркеры строк-итогов, после которых данные не читаются
        private static readonly string[] SummaryMarkers =
        {
            "Итого:",
            "Всего:",
            "ИТОГО:",
            "Итого по договору",
            "Сумма зачета взаимных требований составляет"
        };

        /// <summary>
        /// Основной метод парсинга. Читает CSV-файл и извлекает
        /// строки реестра по конфигурации кредитора.
        /// </summary>
        public ParseResult Parse(string filePath, char delimiter, CreditorConfig config)
        {
            var result = new ParseResult
            {
                CreditorCode = config?.CreditorName ?? "",
                CreditorName = config?.CreditorName ?? ""
            };

            if (config == null)
            {
                result.Success = false;
                result.ErrorMessage = "Конфигурация кредитора не задана.";
                return result;
            }

            try
            {
                // Чтение файла с автоопределением кодировки
                var encodings = new Encoding[]
                {
                    Encoding.UTF8,
                    Encoding.GetEncoding(1251),
                    Encoding.GetEncoding(866)
                };

                string[] lines = FileReader.ReadLines(filePath, encodings);

                // Особый случай: реестр без расшифровки
                if (config.NoRegistryDetails)
                {
                    result.Success = true;
                    result.TotalRows = 0;
                    result.TotalSum = 0;
                    return result;
                }

                // Определяем, используется ли индексный или именной маппинг
                bool useIndexMapping = config.SumSourceColumnIndex >= 0 && config.DocNumberSourceIndex >= 0;

                int headerRowIndex = -1;
                int sumColIndex = -1;
                int docNumColIndex = -1;
                int dateColIndex = -1;
                int filterColIndex = -1;
                int fallbackDocNumColIndex = -1;
                List<string> headerCells = null;

                if (useIndexMapping)
                {
                    sumColIndex = config.SumSourceColumnIndex;
                    docNumColIndex = config.DocNumberSourceIndex;
                    dateColIndex = config.DocDateSourceIndex;

                    // Для индексного маппинга ищем первую строку данных
                    headerRowIndex = FindFirstDataRow(lines, delimiter, config);
                    if (headerRowIndex < 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "Не найдены строки с данными (индексный маппинг).";
                        return result;
                    }
                }
                else
                {
                    // Именной маппинг — ищем строку заголовков
                    headerRowIndex = FindHeaderRow(lines, delimiter, config);
                    if (headerRowIndex < 0)
                    {
                        result.Success = false;
                        result.ErrorMessage =
                            $"Не найдена строка заголовков в файле. " +
                            $"Ищем: SumCol=\"{config.SumSourceColumn}\", DocCol=\"{config.DocNumberSourceColumn}\"";
                        return result;
                    }

                    headerCells = ParseCsvLine(lines[headerRowIndex], delimiter);

                    sumColIndex = FindColumnByPartialName(headerCells, config.SumSourceColumn);
                    docNumColIndex = FindColumnByPartialName(headerCells, config.DocNumberSourceColumn);

                    if (!string.IsNullOrEmpty(config.DocDateSourceColumn))
                        dateColIndex = FindColumnByPartialName(headerCells, config.DocDateSourceColumn);

                    if (!string.IsNullOrEmpty(config.DocNumberFallbackColumn))
                        fallbackDocNumColIndex = FindColumnByPartialName(headerCells, config.DocNumberFallbackColumn);

                    if (!string.IsNullOrEmpty(config.FilterColumnName))
                        filterColIndex = FindColumnByPartialName(headerCells, config.FilterColumnName);

                    if (sumColIndex < 0)
                    {
                        result.Success = false;
                        result.ErrorMessage =
                            $"Не найден столбец суммы «{config.SumSourceColumn}» в заголовке. " +
                            $"Заголовки: [{string.Join("; ", headerCells)}]";
                        return result;
                    }

                    if (docNumColIndex < 0 && fallbackDocNumColIndex < 0 && !config.UseSapForDocNumber)
                    {
                        result.Success = false;
                        result.ErrorMessage =
                            $"Не найден столбец номера документа «{config.DocNumberSourceColumn}» в заголовке. " +
                            $"Заголовки: [{string.Join("; ", headerCells)}]";
                        return result;
                    }
                }

                // Парсинг строк данных
                int skipped = 0;
                int maxColIndex = Math.Max(sumColIndex, Math.Max(docNumColIndex, dateColIndex));
                if (filterColIndex > maxColIndex) maxColIndex = filterColIndex;
                if (fallbackDocNumColIndex > maxColIndex) maxColIndex = fallbackDocNumColIndex;

                // Сохраняем текст заголовка для обнаружения повторных заголовков
                string headerText = headerRowIndex >= 0 && headerRowIndex < lines.Length
                    ? lines[headerRowIndex].Trim() : "";

                for (int i = headerRowIndex + 1; i < lines.Length; i++)
                {
                    string rawLine = lines[i];
                    string line = rawLine.Trim();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (line.Contains("Передан через"))
                    {
                        string[] parts = line.Split(';');
                        string first = parts[0].Trim();
                        string second = parts[1].Trim();
                        string third = parts[2];
                        var match = Regex.Match(
                            third,
                            @"(?<!\d)\d{1,3}(?:[ \u00A0]\d{3})+(?:,\d{2}|[ \u00A0]\d{2})?"
                        );

                        string sum = string.Empty;

                        if (match.Success)
                        {
                            sum = match.Value.Trim();

                            // если дробная часть отделена пробелом — меняем на запятую
                            sum = Regex.Replace(
                                sum,
                                @"([ \u00A0])(\d{2})$",
                                ",$2"
                            );
                        }

                        line = $"{first};{second};{sum};";
                    }

                    // Проверяем маркеры цифровых подписей / штампов
                    if (IsStampOrSignature(line))
                    {
                        skipped++;
                        continue;
                    }

                    // Проверяем ключевые слова исключения
                    if (ShouldExcludeLine(line, config))
                    {
                        skipped++;
                        continue;
                    }

                    // Проверяем маркеры итоговых строк — после них данные
                    // текущей таблицы заканчиваются. Прерываем цикл,
                    // чтобы не включить данные из второй таблицы (если она есть).
                    if (IsSummaryLine(line))
                    {
                        skipped++;
                        break;
                    }

                    var cells = ParseCsvLine(line, delimiter);

                    // Если строка совпадает с заголовком — пропускаем (повторный заголовок
                    // на новой странице или вторая таблица)
                    if (!string.IsNullOrEmpty(headerText) && line.Length > 10)
                    {
                        string normalizedLine = line.Replace(" ", "").Replace("\"", "");
                        string normalizedHeader = headerText.Replace(" ", "").Replace("\"", "");
                        if (normalizedLine == normalizedHeader)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    // Проверяем достаточное количество столбцов
                    if (cells.Count <= maxColIndex)
                        continue;

                    // Извлекаем сумму
                    string sumStr = cells[sumColIndex]?.Trim() ?? "";

                    // Пропускаем строки, где столбец суммы совпадает с названием столбца
                    // (дублирующий заголовок на новой странице)
                    if (string.IsNullOrEmpty(sumStr) || !string.IsNullOrEmpty(config.SumSourceColumn) &&
                        sumStr.IndexOf(config.SumSourceColumn, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        skipped++;
                        continue;
                    }

                    decimal sumValue = ParseNumber(sumStr, config);

                    // Извлекаем номер документа
                    string docNumber = "";
                    if (config.UseSapForDocNumber)
                    {
                        docNumber = "";
                    }
                    else if (docNumColIndex >= 0 && docNumColIndex < cells.Count)
                    {
                        docNumber = cells[docNumColIndex]?.Trim() ?? "";
                    }

                    // Fallback на запасной столбец номера документа
                    if (string.IsNullOrWhiteSpace(docNumber) && fallbackDocNumColIndex >= 0
                        && fallbackDocNumColIndex < cells.Count)
                    {
                        docNumber = cells[fallbackDocNumColIndex]?.Trim() ?? "";
                    }
                    string docNumberClean = Regex.Replace(docNumber, @"[^0-9\\/]", "");
                    if (docNumberClean.Length < 5)
                    {
                        continue;
                    }

                    // Извлекаем дату документа
                    string docDate = "";
                    if (dateColIndex >= 0 && dateColIndex < cells.Count)
                        docDate = cells[dateColIndex]?.Trim() ?? "";

                    // Применяем фильтр по столбцу (например, В/Сч = "K")
                    if (filterColIndex >= 0 && filterColIndex < cells.Count
                        && !string.IsNullOrEmpty(config.FilterValue))
                    {
                        string filterVal = cells[filterColIndex]?.Trim() ?? "";
                        if (!filterVal.Equals(config.FilterValue, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    // Пропускаем пустые строки (нет ни суммы, ни номера документа)
                    if (sumValue == 0 && string.IsNullOrWhiteSpace(docNumber))
                    {
                        skipped++;
                        continue;
                    }

                    // Если номер документа пустой, но сумма есть — это строка-итог
                    // (без номера документа). Пропускаем, чтобы не включать
                    // промежуточные итоги в данные.
                    if (string.IsNullOrWhiteSpace(docNumber) && sumValue != 0
                        && !config.UseSapForDocNumber)
                    {
                        skipped++;
                        continue;
                    }

                    // Обработка номера документа: извлечь часть до даты
                    if (config.ParseDocNumberBeforeDate && !string.IsNullOrEmpty(docNumber))
                    {
                        docNumber = ExtractDocNumberBeforeDate(docNumber);
                    }

                    // Форматируем дату
                    if (!string.IsNullOrWhiteSpace(docDate))
                    {
                        docDate = NormalizeDate(docDate);
                    }

                    var row = new ParsedRegistryRow
                    {
                        DocNumber = docNumber,
                        DocDate = docDate,
                        Sum = sumValue,
                        RawLine = rawLine
                    };

                    result.Rows.Add(row);
                }

                result.TotalRows = result.Rows.Count;
                result.TotalSum = result.Rows.Sum(r => r.Sum);
                result.SkippedRows = skipped;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message + (ex.InnerException != null
                    ? " | " + ex.InnerException.Message : "");
            }

            return result;
        }

        // =================================================================
        //  Поиск строки заголовков
        // =================================================================

        /// <summary>
        /// Ищет строку заголовков по названиям столбцов из конфига.
        /// Поддерживает частичное совпадение и синонимы.
        /// </summary>
        private int FindHeaderRow(string[] lines, char delimiter, CreditorConfig config)
        {
            string searchText1 = config.SumSourceColumn ?? "";
            string searchText2 = config.DocNumberSourceColumn ?? "";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Пропускаем строки-штампы
                if (IsStampOrSignature(line))
                    continue;

                bool match1 = string.IsNullOrEmpty(searchText1) ||
                              ContainsWithSynonyms(line, searchText1);
                bool match2 = string.IsNullOrEmpty(searchText2) ||
                              ContainsWithSynonyms(line, searchText2);

                if (match1 && match2)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Проверяет вхождение текста с учётом типичных синонимов:
        /// «зачетного» ↔ «зачтенного», «Номер» ↔ «№» и т.д.
        /// </summary>
        private bool ContainsWithSynonyms(string line, string searchText)
        {
            if (line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Синоним «зачетного» ↔ «зачтенного»
            string alt = searchText;
            if (alt.Contains("зачетного"))
            {
                alt = alt.Replace("зачетного", "зачтенного");
                if (line.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (alt.Contains("зачтенного"))
            {
                alt = alt.Replace("зачтенного", "зачетного");
                if (line.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // Синоним «Номер» ↔ «№»
            alt = searchText;
            if (alt.Contains("Номер"))
            {
                alt = alt.Replace("Номер", "№");
                if (line.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            if (alt.Contains("№"))
            {
                alt = alt.Replace("№", "Номер");
                if (line.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Для индексного маппинга — ищет первую строку, похожую на данные.
        /// </summary>
        private int FindFirstDataRow(string[] lines, char delimiter, CreditorConfig config)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (IsStampOrSignature(line))
                    continue;
                if (LooksLikeHeaderText(line))
                    continue;

                var cells = ParseCsvLine(line, delimiter);
                int maxIdx = Math.Max(config.SumSourceColumnIndex, config.DocNumberSourceIndex);

                if (cells.Count <= maxIdx)
                    continue;

                string potentialDoc = cells[config.DocNumberSourceIndex]?.Trim() ?? "";
                string potentialSum = cells[config.SumSourceColumnIndex]?.Trim() ?? "";

                if (!string.IsNullOrWhiteSpace(potentialDoc) || ContainsDigits(potentialSum))
                    return i;
            }

            return -1;
        }

        // =================================================================
        //  Определение индексов столбцов по имени
        // =================================================================

        /// <summary>
        /// Ищет столбец по частичному совпадению имени с учётом синонимов.
        /// </summary>
        private int FindColumnByPartialName(List<string> headers, string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                return -1;

            // Точное совпадение (без учёта регистра)
            for (int i = 0; i < headers.Count; i++)
            {
                if (headers[i].Trim().Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            // Частичное совпадение с синонимами
            for (int i = 0; i < headers.Count; i++)
            {
                string header = headers[i].Trim();
                if (ContainsWithSynonyms(header, columnName))
                    return i;

                // Также проверяем обратное: заголовок содержит искомое
                if (header.IndexOf(columnName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            return -1;
        }

        // =================================================================
        //  Парсинг чисел
        // =================================================================

        /// <summary>
        /// Парсит число из строки реестра. Поддерживает форматы:
        /// - «23 878,14» (пробел — разделитель тысяч, запятая — десятичный)
        /// - «1.535.138,50» (точка — разделитель тысяч)
        /// - «-4 145,28» (ведущий минус)
        /// - «317.436,63-» (хвостовой минус — Метро)
        /// - «- 1 410,55» (минус с пробелом — Лента)
        /// </summary>
        private decimal ParseNumber(string raw, CreditorConfig config)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            string s = raw.Trim();

            // Удаляем символ рубля и «руб.»
            s = Regex.Replace(s, @"руб\.?", "", RegexOptions.IgnoreCase);
            s = s.Trim();

            // Хвостовой минус: «317.436,63-» → «-317.436,63»
            bool isNegative = false;
            if (s.EndsWith("-") && s.Length > 1)
            {
                isNegative = true;
                s = s.Substring(0, s.Length - 1).Trim();
            }

            // Ведущий минус с пробелом: «- 1 410,55» → «-1 410,55»
            if (s.StartsWith("-") && s.Length > 1 && s[1] == ' ')
                s = "-" + s.Substring(1).TrimStart();

            // Если точка используется как разделитель тысяч:
            // «1.535.138,50» → «1535138,50»
            if (config.DotAsThousandsSeparator)
            {
                s = s.Replace(".", "");
            }

            // Пробел как разделитель тысяч: «23 878,14» → «23878,14»
            s = s.Replace(" ", "");

            // Заменяем запятую на точку для InvariantCulture
            s = s.Replace(",", ".");

            // Очищаем от нецифровых символов, сохраняя точку и минус
            var sb = new StringBuilder();
            bool hasDot = false;
            bool hasMinus = s.StartsWith("-");

            if (hasMinus)
                sb.Append('-');

            foreach (char c in s.Skip(hasMinus ? 1 : 0))
            {
                if (char.IsDigit(c))
                {
                    sb.Append(c);
                }
                else if (c == '.' && !hasDot)
                {
                    sb.Append(c);
                    hasDot = true;
                }
            }

            s = sb.ToString();

            if (isNegative && !s.StartsWith("-"))
                s = "-" + s;

            decimal result;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
                return result;

            return 0;
        }

        // =================================================================
        //  Парсинг и нормализация дат
        // =================================================================

        /// <summary>
        /// Пытается распарсить дату и возвращает её в формате dd.MM.yyyy.
        /// </summary>
        private string NormalizeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            DateTime dt;
            if (DateHelper.TryParse(raw, out dt))
                return dt.ToString("dd.MM.yyyy");

            return raw;
        }

        /// <summary>
        /// Извлекает номер документа из строки вида
        /// «8031937940 06.12.2024» → «8031937940».
        /// </summary>
        private string ExtractDocNumberBeforeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            // Ищем паттерн: текст, затем пробел, затем дата
            var match = Regex.Match(raw, @"^(.+?)\s+\d{1,2}[./]\d{1,2}[./]\d{2,4}");
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // Если даты нет — берём до первого пробела
            int spaceIdx = raw.IndexOf(' ');
            if (spaceIdx > 0)
                return raw.Substring(0, spaceIdx).Trim();

            return raw.Trim();
        }

        // =================================================================
        //  Фильтрация строк
        // =================================================================

        /// <summary>
        /// Проверяет, нужно ли исключить строку по ключевым словам из конфига.
        /// </summary>
        private bool ShouldExcludeLine(string line, CreditorConfig config)
        {
            if (config.ExcludeRowKeywords == null)
                return false;

            foreach (var keyword in config.ExcludeRowKeywords)
            {
                if (!string.IsNullOrEmpty(keyword) &&
                    line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Проверяет, похожа ли строка на цифровой штамп/подпись.
        /// </summary>
        private bool IsStampOrSignature(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            foreach (var marker in StampMarkers)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Проверяет, является ли строка итоговой (Итого, Всего и т.п.).
        /// </summary>
        private bool IsSummaryLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            foreach (var marker in SummaryMarkers)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Проверяет, похожа ли строка на заголовок/текст, а не на данные.
        /// Используется для индексного маппинга (Метро).
        /// </summary>
        private bool LooksLikeHeaderText(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            // Типичные фразы из текстовых частей документов
            string[] textMarkers = {
                "Уведомление о зачете",
                "Заявление о зачете",
                "АКТ зачета",
                "зачета взаимных",
                "Уважаемый",
                "Настоящим",
                "АКЦИОНЕРНОЕ ОБЩЕСТВО",
                "ГК РФ",
                "подпись",
                "М.П.",
                "должность",
                "ИНН",
                "КПП",
                "БИК",
                "Р/С",
                "К/С",
                "расшифровка",
                "Авизо",
                "КОНТРАГЕНТ",
                "Перенос",
                "Оплачено",
                "Зачет встречных",
                "корректировка",
                "ПРИМЕЧАНИЯ",
                "Получатель",
                "Наш исполнитель",
                "Телефон",
                "Контрагент",
                "Многоуважаемые",
                "Руководитель",
                "Главный бухгалтер",
                "ООО \"МЕТРО",
                "ООО \"ТК",
                "Платежный документ",
                "ВидДок"
            };

            foreach (var marker in textMarkers)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        // =================================================================
        //  Вспомогательные методы
        // =================================================================

        private bool ContainsDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            return s.Any(char.IsDigit);
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
    }
}
