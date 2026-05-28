using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CsvParsing
{
    /// <summary>
    /// Строка CSV-таблицы. Хранит ячейки по индексу столбца.
    /// </summary>
    public class CsvRow
    {
        private readonly List<string> _cells;

        /// <summary>
        /// Доступ к ячейке по индексу столбца.
        /// </summary>
        public string this[int columnIndex]
        {
            get
            {
                if (columnIndex < 0 || columnIndex >= _cells.Count)
                    throw new ArgumentOutOfRangeException(nameof(columnIndex),
                        $"Столбец {columnIndex} вне диапазона. Всего столбцов: {_cells.Count}");
                return _cells[columnIndex];
            }
            set
            {
                if (columnIndex < 0 || columnIndex >= _cells.Count)
                    throw new IndexOutOfRangeException(
                        $"Столбец {columnIndex} вне диапазона. Всего столбцов: {_cells.Count}");
                _cells[columnIndex] = value;
            }
        }

        public int CellCount => _cells.Count;

        public CsvRow(int rowId, List<string> cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells),
                $"Ячейки строки не могут быть null (RowId={rowId}).");
        }

        /// <summary>
        /// Возвращает копию списка ячеек.
        /// </summary>
        public List<string> GetCells() => new List<string>(_cells);

        /// <summary>
        /// Пытается получить числовое значение ячейки. Поддерживает форматы с запятой и точкой.
        /// </summary>
        public bool TryGetDouble(int columnIndex, out double result)
        {
            result = 0;
            if (columnIndex < 0 || columnIndex >= _cells.Count)
                return false;

            var raw = Regex.Replace(_cells[columnIndex], @"[^0-9,.-]", "");
            raw = raw.Replace(',', '.');
            raw = raw.TrimEnd('-');

            return double.TryParse(
                raw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out result);
        }

        /// <summary>
        /// Сериализует строку в CSV-формат.
        /// </summary>
        public string ToCsvString(char delimiter)
        {
            return string.Join(delimiter.ToString(), _cells);
        }
    }
}
