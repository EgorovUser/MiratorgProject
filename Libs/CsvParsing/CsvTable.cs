using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XmlParser;

namespace CsvParsing
{
    /// <summary>
    /// CSV-таблица с заголовками, строками и методами обработки данных.
    /// </summary>
    public class CsvTable
    {
        public List<string> Headers { get; }
        public List<CsvRow> Rows { get; private set; }
        private int _nextRowId;
        public int ColumnCount => Headers.Count;

        private readonly CultureInfo _culture = new CultureInfo("ru-RU");

        public CsvTable(List<string> headers)
        {
            Headers = headers ?? throw new ArgumentNullException(nameof(headers),
                "Заголовки таблицы не могут быть null.");
            if (headers.Count == 0)
                throw new ArgumentException("Таблица должна содержать хотя бы один столбец.");
            Rows = new List<CsvRow>();
            _nextRowId = 1;
        }

        /// <summary>
        /// Добавляет строку в таблицу. Автоматически дополняет или обрезает до количества столбцов.
        /// </summary>
        public CsvRow AddRow(List<string> cells)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells), "Ячейки не могут быть null.");

            while (cells.Count < ColumnCount)
                cells.Add("");
            if (cells.Count > ColumnCount)
                cells = cells.Take(ColumnCount).ToList();

            var row = new CsvRow(_nextRowId++, cells);
            Rows.Add(row);
            return row;
        }

        /// <summary>
        /// Нормализует числовые значения в столбце к формату "F2" (ru-RU).
        /// </summary>
        public int NormalizeNumbers(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            int count = 0;
            foreach (var row in Rows)
            {
                if (row.TryGetDouble(columnIndex, out double value))
                {
                    row[columnIndex] = value.ToString("F2", _culture);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Нормализует значения в столбце к формату даты dd.MM.yyyy.
        /// Поддерживает Unix-время, ISO, различные форматы.
        /// </summary>
        public int NormalizeDates(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            int count = 0;
            foreach (var row in Rows)
            {
                var raw = row[columnIndex]?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (raw.Length < 6) continue;

                var normalized = TryNormalizeDate(raw);
                if (normalized != null)
                {
                    row[columnIndex] = normalized;
                    count++;
                }
            }
            return count;
        }

        private string TryNormalizeDate(string raw)
        {
            raw = raw.Trim();

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixSeconds))
            {
                if (unixSeconds > 0 && unixSeconds < 4_200_000_000)
                {
                    return DateTimeOffset
                        .FromUnixTimeSeconds(unixSeconds)
                        .ToString("dd.MM.yyyy");
                }
            }

            string[] formats =
            {
                "yyyy-MM-dd",
                "dd/MM/yyyy",
                "MM/dd/yyyy",
                "dd.MM.yyyy",
                "yyyyMMdd"
            };

            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            {
                return date.ToString("dd.MM.yyyy");
            }

            if (DateTime.TryParse(raw, out date))
            {
                return date.ToString("dd.MM.yyyy");
            }

            throw new ArgumentException("Значение не распознано как дата: \"" + raw + "\".");
        }

        /// <summary>
        /// Находит самую раннюю дату в указанном столбце и возвращает её в формате dd.MM.yyyy.
        /// </summary>
        public string FindEarliestDateRow(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            int earliestRow = -1;
            DateTime earliestDate = DateTime.MaxValue;

            for (int i = 0; i < Rows.Count; i++)
            {
                var value = Rows[i][columnIndex]?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!DateTime.TryParseExact(value, "dd.MM.yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                    continue;

                if (date < earliestDate)
                {
                    earliestDate = date;
                    earliestRow = i;
                }
            }

            if (earliestRow == -1)
                throw new InvalidOperationException("В столбце нет корректных дат.");

            return Rows[earliestRow][columnIndex];
        }

        /// <summary>
        /// Находит дубликаты строк по значению столбца. Возвращает словарь: значение -> список индексов строк.
        /// </summary>
        public Dictionary<string, List<int>> FindDuplicateRows(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            return Rows
                .Select((row, index) => new { row, index })
                .GroupBy(x => (x.row[columnIndex] ?? "").Trim())
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() >= 2)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.index).ToList()
                );
        }

        /// <summary>
        /// Суммирует значения в столбце для указанных строк.
        /// </summary>
        public double SumValues(int columnIndex, List<int> sourceRowIds)
        {
            ValidateColumnIndex(columnIndex);

            double sum = 0;
            foreach (var rowId in sourceRowIds)
            {
                var sourceRow = GetRowById(rowId);
                if (sourceRow.TryGetDouble(columnIndex, out double val))
                    sum += val;
                else
                    throw new FormatException(
                        $"Значение в строке {rowId}, столбец {columnIndex} не является числом: \"{sourceRow[columnIndex]}\".");
            }
            return sum;
        }

        /// <summary>
        /// Удаляет строки по списку индексов.
        /// </summary>
        public void DeleteRows(List<int> rowIds)
        {
            if (rowIds == null)
                throw new ArgumentNullException(nameof(rowIds), "Список rowIds не может быть null.");

            foreach (var index in rowIds.OrderByDescending(x => x))
                Rows.RemoveAt(index);
        }

        /// <summary>
        /// Возвращает общее значение столбца, если оно одинаково для всех строк.
        /// </summary>
        public string GetCommonValue(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            if (Rows.Count == 0)
                return null;

            string commonValue = Rows[0][columnIndex];

            for (int i = 1; i < Rows.Count; i++)
            {
                if ((Rows[i][columnIndex] ?? "") != (commonValue ?? ""))
                    return null;
            }

            return commonValue;
        }

        /// <summary>
        /// Сравнивает сумму столбца 8 с общей суммой и корректирует таблицу.
        /// При положительной разнице добавляет строки из xmlRows для компенсации.
        /// </summary>
        public bool Comparison(List<XmlRow> xmlRows)
        {
            if (Rows.Count == 0)
                throw new InvalidOperationException("Таблица не содержит строк для сравнения.");

            var sum = GetColumnSum(8);
            double commonSum = GetCellValueAsDouble(0, 4);
            double diff = commonSum - sum;

            if (diff == 0)
                return true;

            if (diff < 0)
            {
                double lastRowValue = GetCellValueAsDouble(Rows.Count - 1, 8);
                double needToRemove = Math.Abs(diff);
                if (lastRowValue > needToRemove)
                {
                    string newValue = (lastRowValue - needToRemove).ToString("F2", new CultureInfo("ru-RU"));
                    SetCellValue(Rows.Count - 1, 8, newValue);
                    return true;
                }
                else
                {
                    DeleteRows(new List<int> { Rows.Count - 1 });
                    return Comparison(xmlRows);
                }
            }
            else
            {
                if (xmlRows.Count == 0)
                    return false;

                var firstXmlRow = xmlRows[0];
                xmlRows.Remove(firstXmlRow);
                var newCells = new List<string>();

                for (int col = 0; col < ColumnCount; col++)
                {
                    if (col < 5)
                    {
                        var common = GetCommonValue(col);
                        newCells.Add(common ?? Rows[0][col]);
                    }
                    else if (col == 5)
                    {
                        newCells.Add(firstXmlRow.GetValue("DocNumber") ?? "");
                    }
                    else if (col == 6)
                    {
                        newCells.Add(firstXmlRow.GetValue("DocDate") ?? "");
                    }
                    else if (col == 7 || col == 8)
                    {
                        string effectiveAmountStr = firstXmlRow.GetValue("EffectiveAmount");
                        double effectiveAmount = 0;

                        if (effectiveAmountStr != null)
                        {
                            var raw = Regex.Replace(effectiveAmountStr, @"[^0-9,.-]", "");
                            raw = raw.Replace(',', '.');
                            if (raw.EndsWith("-"))
                                raw = '-' + raw.TrimEnd('-');

                            double.TryParse(raw, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out effectiveAmount);
                        }

                        newCells.Add(effectiveAmount.ToString("F2", new CultureInfo("ru-RU")));
                    }
                    else
                    {
                        newCells.Add("");
                    }
                }
                AddRow(newCells);
                return Comparison(xmlRows);
            }
        }

        /// <summary>
        /// Распределяет отрицательные значения в столбце по положительным (от самых больших к меньшим).
        /// После распределения отрицательные строки удаляются.
        /// </summary>
        public bool SubtractWithCheck(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            var negativeRows = new List<int>();
            var positiveRows = new List<(CsvRow Row, double Value)>();

            double negativeSum = 0;

            foreach (var row in Rows)
            {
                if (!row.TryGetDouble(columnIndex, out double val))
                    continue;

                if (val < 0)
                {
                    negativeRows.Add(Rows.IndexOf(row));
                    negativeSum += val;
                }
                else if (val > 0)
                {
                    positiveRows.Add((row, val));
                }
            }

            if (negativeRows.Count == 0 || positiveRows.Count == 0)
                return false;

            double deficit = Math.Abs(negativeSum);

            foreach (var item in positiveRows.OrderByDescending(x => x.Value))
            {
                if (deficit <= 0)
                    break;

                double newValue = item.Value - deficit;

                if (newValue >= 0)
                {
                    item.Row[columnIndex] = newValue.ToString("F2", _culture);
                    deficit = 0;
                    break;
                }
                else
                {
                    item.Row[columnIndex] = 0.00.ToString("F2", _culture);
                    deficit = Math.Abs(newValue);
                }
            }

            if (deficit > 0)
                throw new InvalidOperationException("Недостаточно положительных значений для компенсации отрицательных.");

            DeleteRows(negativeRows);
            return true;
        }

        /// <summary>
        /// Возвращает числовое значение ячейки, или 0, если значение не является числом.
        /// </summary>
        public double GetCellValueAsDouble(int rowId, int columnIndex)
        {
            ValidateColumnIndex(columnIndex);
            var row = GetRowById(rowId);
            if (row.TryGetDouble(columnIndex, out double value))
                return value;
            return 0;
        }

        /// <summary>
        /// Устанавливает значение ячейки по индексам строки и столбца.
        /// </summary>
        public void SetCellValue(int rowId, int columnIndex, string value)
        {
            ValidateColumnIndex(columnIndex);
            var row = GetRowById(rowId);
            row[columnIndex] = value;
        }

        /// <summary>
        /// Возвращает сумму всех числовых значений в столбце.
        /// </summary>
        public double GetColumnSum(int columnIndex)
        {
            ValidateColumnIndex(columnIndex);

            double sum = 0;
            foreach (var row in Rows)
            {
                if (row.TryGetDouble(columnIndex, out double val))
                    sum += val;
            }
            return sum;
        }

        /// <summary>
        /// Сериализует таблицу в XML.
        /// </summary>
        public string ToXml()
        {
            var tableElement = new XElement("Table",
                new XAttribute("Columns", ColumnCount),
                new XAttribute("Rows", Rows.Count));

            var headersElement = new XElement("Headers");
            foreach (var header in Headers)
            {
                headersElement.Add(new XElement("Column",
                    new XAttribute("Index", Headers.IndexOf(header)), header));
            }
            tableElement.Add(headersElement);

            foreach (var row in Rows)
            {
                var rowElement = new XElement("Row",
                    new XAttribute("RowId", Rows.IndexOf(row)));
                for (int i = 0; i < row.CellCount; i++)
                {
                    var colElement = new XElement("Column",
                        new XAttribute("Name", Headers[i]),
                        new XAttribute("Index", i),
                        row[i] ?? "");
                    rowElement.Add(colElement);
                }
                tableElement.Add(rowElement);
            }

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                tableElement);

            return doc.ToString();
        }

        /// <summary>
        /// Сохраняет таблицу в XML-файл.
        /// </summary>
        public void SaveToXml(string filePath)
        {
            var xml = ToXml();
            File.WriteAllText(filePath, xml, Encoding.UTF8);
        }

        /// <summary>
        /// Сериализует таблицу в CSV-строку.
        /// </summary>
        /// <param name="delimiter">Разделитель столбцов.</param>
        /// <param name="lineSeparator">Разделитель строк. По умолчанию Environment.NewLine.</param>
        public string ToCsvString(char delimiter, string lineSeparator = null)
        {
            var sb = new StringBuilder();
            string sep = lineSeparator ?? Environment.NewLine;

            sb.Append(string.Join(delimiter.ToString(), Headers));
            sb.Append(sep);

            foreach (var row in Rows)
            {
                sb.Append(row.ToCsvString(delimiter));
                sb.Append(sep);
            }

            return sb.ToString();
        }

        private void ValidateColumnIndex(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= ColumnCount)
                throw new ArgumentOutOfRangeException(nameof(columnIndex),
                    $"Столбец {columnIndex} вне диапазона. Всего столбцов: {ColumnCount}.");
        }

        private CsvRow GetRowById(int rowId)
        {
            if (Rows.Count <= rowId || rowId < 0)
                throw new ArgumentOutOfRangeException(nameof(rowId),
                    $"Строка {rowId} вне диапазона. Всего строк: {Rows.Count}.");
            return Rows[rowId];
        }
    }
}
