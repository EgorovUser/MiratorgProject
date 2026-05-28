using CommonLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TxtTableParsing
{
    /// <summary>
    /// Парсер табличных файлов с разделителями (по умолчанию — табуляция).
    ///
    /// Типичный сценарий — выгрузки из SAP ALV в формате «Текст с табуляторами».
    ///
    /// Порядок работы:
    ///   1. Создать экземпляр: <code>new TabDelimitedParser(path, options)</code>
    ///   2. Вызвать <see cref="Parse"/> — возвращает <see cref="ParsedTable"/>
    ///   3. Работать с результатом: итерация, фильтрация, группировка.
    ///
    /// Парсер автоматически:
    ///   • Определяет кодировку (Windows-1251, UTF-8 и др.)
    ///   • Находит строку заголовков по обязательным столбцам
    ///   • Пропускает итоговые строки (***, **)
    ///   • Проверяет строки на наличие валидного года (опционально)
    /// </summary>
    public class TabDelimitedParser
    {
        private readonly string _filePath;
        private readonly ParserOptions _options;

        /// <summary>
        /// Создаёт парсер с указанным файлом и настройками.
        /// </summary>
        /// <param name="filePath">Путь к табличному файлу</param>
        /// <param name="options">Настройки парсинга (null = значения по умолчанию)</param>
        public TabDelimitedParser(string filePath, ParserOptions options = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _options = options ?? new ParserOptions();
        }

        /// <summary>
        /// Парсит файл и возвращает таблицу с заголовками и строками.
        /// </summary>
        /// <returns><see cref="ParsedTable"/> с результатом парсинга</returns>
        public ParsedTable Parse()
        {
            if (!File.Exists(_filePath))
                throw new FileNotFoundException($"Файл не найден: {_filePath}");

            var sw = Stopwatch.StartNew();

            // 1. Чтение файла
            string[] lines = FileReader.ReadLines(_filePath, _options.Encodings);

            // 2. Поиск строки заголовков
            int headerIndex = FindHeaderRow(lines);
            if (headerIndex < 0)
            {
                string required = _options.RequiredColumns.Length > 0
                    ? string.Join(", ", _options.RequiredColumns)
                    : "(не заданы)";

                throw new InvalidOperationException(
                    $"Строка заголовков не найдена в файле.{Environment.NewLine}" +
                    $"Обязательные столбцы: {required}{Environment.NewLine}" +
                    $"Файл: {_filePath}" +
                    $"Текст: {string.Join(Environment.NewLine, lines)}");
            }

            // 3. Построение маппинга столбцов
            string[] headerFields = lines[headerIndex].Split(_options.Delimiter);
            var columnIndex = BuildColumnIndex(headerFields);

            // 4. Проверка обязательных столбцов
            ValidateRequiredColumns(columnIndex, headerFields);

            // 5. Извлечение строк данных
            var rows = ExtractDataRows(lines, headerIndex + 1, columnIndex);

            sw.Stop();
            Debug.WriteLine(
                $"[TabDelimitedParser] {rows.Count} строк извлечено за {sw.ElapsedMilliseconds} мс " +
                $"(заголовок на строке {headerIndex + 1})");

            return new ParsedTable(headerFields, rows, headerIndex, _filePath);
        }

        /// <summary>
        /// Парсит файл за один вызов (удобный статический метод).
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="requiredColumns">Обязательные столбцы в заголовке</param>
        /// <param name="options">Настройки парсинга (null = по умолчанию)</param>
        /// <returns>Результат парсинга</returns>
        public static ParsedTable ParseFile(
            string filePath,
            string[] requiredColumns = null,
            ParserOptions options = null)
        {
            var opts = options ?? new ParserOptions();

            if (requiredColumns != null && requiredColumns.Length > 0)
                opts.RequiredColumns = requiredColumns;

            var parser = new TabDelimitedParser(filePath, opts);
            return parser.Parse();
        }

        /// <summary>
        /// Ищет строку заголовков среди первых MaxHeaderSearchRows строк.
        /// Заголовком считается строка, содержащая ВСЕ столбцы из RequiredColumns.
        /// Если RequiredColumns пуст — берётся первая непустая строка.
        /// </summary>
        private int FindHeaderRow(string[] lines)
        {
            // Если обязательные столбцы не заданы — берём первую непустую строку
            if (_options.RequiredColumns.Length == 0)
            {
                for (int i = 0; i < Math.Min(lines.Length, _options.MaxHeaderSearchRows); i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        return i;
                }

                return -1;
            }

            // Ищем строку, содержащую все обязательные столбцы
            int maxRows = Math.Min(lines.Length, _options.MaxHeaderSearchRows);

            for (int i = 0; i < maxRows; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = line.Split(_options.Delimiter);
                var fieldSet = new HashSet<string>(
                    Array.ConvertAll(fields, f => f.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                bool allFound = true;
                foreach (var required in _options.RequiredColumns)
                {
                    if (!fieldSet.Contains(required))
                    {
                        allFound = false;
                        break;
                    }
                }

                if (allFound)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Строит словарь {имя столбца → индекс} (без учёта регистра).
        /// Если есть дубликаты имён — берётся первое вхождение.
        /// </summary>
        private Dictionary<string, int> BuildColumnIndex(string[] headers)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i].Trim();
                if (!string.IsNullOrWhiteSpace(name) && !index.ContainsKey(name))
                {
                    index[name] = i;
                }
            }

            return index;
        }

        /// <summary>
        /// Проверяет, что все обязательные столбцы найдены в маппинге.
        /// При отсутствии — выбрасывает исключение с перечислением недостающих и доступных столбцов.
        /// </summary>
        private void ValidateRequiredColumns(Dictionary<string, int> columnIndex, string[] rawHeaders)
        {
            if (_options.RequiredColumns.Length == 0)
                return;

            var missing = new List<string>();
            foreach (var col in _options.RequiredColumns)
            {
                if (!columnIndex.ContainsKey(col))
                    missing.Add(col);
            }

            if (missing.Count > 0)
            {
                var available = new List<string>();
                foreach (var h in rawHeaders)
                {
                    string trimmed = h.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        available.Add(trimmed);
                }

                string availableList = string.Join(", ", available.Take(30));
                if (available.Count > 30)
                    availableList += " ...";

                throw new InvalidOperationException(
                    $"Столбцы не найдены: {string.Join(", ", missing)}.{Environment.NewLine}" +
                    $"Доступные столбцы: {availableList}");
            }
        }

        /// <summary>
        /// Извлекает строки данных, начиная со startLine.
        /// Пропускает пустые строки, итоговые (с маркерами SummaryMarkers)
        /// и строки, не прошедшие проверку года.
        /// </summary>
        private List<Row> ExtractDataRows(
            string[] lines,
            int startLine,
            Dictionary<string, int> columnIndex)
        {
            var rows = new List<Row>();

            for (int i = startLine; i < lines.Length; i++)
            {
                string line = lines[i];

                // Пустые строки
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] fields = line.Split(_options.Delimiter);

                // Пропуск итоговых строк
                if (IsSummaryRow(fields))
                    continue;

                // Проверка поля с годом (опционально)
                if (_options.YearFieldIndex.HasValue && !IsValidDataRow(fields))
                    continue;

                for(int j = 0; j < fields.Length; j++)
                {
                    fields[j] = fields[j].Trim();
                }

                rows.Add(new Row(fields, columnIndex));
            }

            Debug.WriteLine($"[TabDelimitedParser] Извлечено {rows.Count} строк из {lines.Length - startLine} прочитанных");
            return rows;
        }

        /// <summary>
        /// Проверяет, является ли строка итоговой (содержит маркер из SummaryMarkers).
        /// </summary>
        private bool IsSummaryRow(string[] fields)
        {
            if (_options.SummaryMarkers == null || _options.SummaryMarkers.Count == 0)
                return false;

            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 3)
                    break;
                else
                {
                    string trimmed = fields[i].Trim();
                    if (_options.SummaryMarkers.Contains(trimmed))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Проверяет, что поле с индексом YearFieldIndex содержит 4-значный год
        /// в диапазоне [MinYear, MaxYear].
        /// </summary>
        private bool IsValidDataRow(string[] fields)
        {
            int idx = _options.YearFieldIndex.Value;

            if (idx < 0 || idx >= fields.Length)
                return false;

            string yearStr = fields[idx].Trim();

            if (!int.TryParse(yearStr, out int year))
                return false;

            return year >= _options.MinYear && year <= _options.MaxYear;
        }
    }
}
