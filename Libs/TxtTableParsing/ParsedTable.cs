using CommonLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TxtTableParsing
{
    /// <summary>
    /// Результат парсинга табличного файла.
    /// Содержит заголовки столбцов, строки данных и метаданные.
    /// После создания объект неизменяем (immutable).
    /// </summary>
    public class ParsedTable
    {
        /// <summary>Заголовки столбцов (в порядке из файла)</summary>
        public IReadOnlyList<string> Headers { get; }

        /// <summary>Строки данных (все, прошедшие фильтрацию)</summary>
        public List<Row> Rows { get; }

        /// <summary>Индекс строки заголовков в исходном файле (0-based)</summary>
        public int HeaderRowIndex { get; }

        /// <summary>Путь к исходному файлу</summary>
        public string SourceFilePath { get; }

        internal ParsedTable(
            string[] headers,
            List<Row> rows,
            int headerRowIndex,
            string sourceFilePath)
        {
            Headers = Array.AsReadOnly(headers ?? Array.Empty<string>());
            Rows = rows ?? new List<Row>();
            HeaderRowIndex = headerRowIndex;
            SourceFilePath = sourceFilePath ?? string.Empty;
        }

        /// <summary>Количество строк данных</summary>
        public int RowCount => Rows.Count;

        /// <summary>Количество столбцов</summary>
        public int ColumnCount => Headers.Count;

        /// <summary>Есть ли данные</summary>
        public bool HasData => Rows.Count > 0;

        /// <summary>Фильтрует строки по условию</summary>
        public List<Row> Where(Func<Row, bool> predicate)
        {
            return Rows.Where(predicate).ToList();
        }

        /// <summary>Группирует строки по значению столбца (без учёта регистра)</summary>
        public IEnumerable<IGrouping<string, Row>> GroupBy(string columnName)
        {
            return Rows.GroupBy(r => r[columnName], StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Возвращает первую строку, удовлетворяющую условию (или null)</summary>
        public Row FirstOrDefault(Func<Row, bool> predicate)
        {
            return Rows.FirstOrDefault(predicate);
        }

        /// <summary>Возвращает все строки, у которых поле columnName содержит указанное значение</summary>
        public List<Row> WhereEquals(string columnName, string value)
        {
            return Rows
                .Where(r => r.EqualsValue(columnName, value))
                .ToList();
        }

        /// <summary>Возвращает уникальные значения указанного столбца</summary>
        public HashSet<string> DistinctValues(string columnName)
        {
            return new HashSet<string>(
                Rows.Select(r => r[columnName]),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Сортирует строки по дате в указанном столбце (формат dd.MM.yyyy) 
        /// и возвращает строку с самой ранней датой. Если список пуст — null.
        /// </summary>
        public static Row OrderByDateTakeFirst(List<Row> rows, string dateColumnName)
        {
            if (rows == null || rows.Count == 0)
                return null;

            return rows
                .OrderBy(r =>
                {
                    string dateStr = r[dateColumnName];
                    if (DateTime.TryParseExact(dateStr, "dd.MM.yyyy",
                        null, System.Globalization.DateTimeStyles.None, out var date))
                        return date;
                    return DateTime.MaxValue; // невалидные даты — в конец
                })
                .First();
        }


        /// <summary>
        /// Возвращает строку (Row) с самой ранней датой в указанном столбце.
        /// </summary>
        public static Row GetEarliestRow(List<Row> rows, string dateColumn)
        {
            Row earliest = null;
            DateTime? earliestDate = null;

            foreach (var row in rows)
            {
                DateTime? parsed = DateHelper.TryParseNullable(row[dateColumn]);

                if (parsed.HasValue && (!earliestDate.HasValue || parsed.Value < earliestDate.Value))
                {
                    earliestDate = parsed.Value;
                    earliest = row;
                }
            }

            return earliest;
        }
    }
}
