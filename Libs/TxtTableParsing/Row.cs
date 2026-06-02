using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TxtTableParsing
{
    /// <summary>
    /// Представляет одну строку данных из табличного файла.
    /// Обеспечивает безопасный доступ к полям по имени столбца или по индексу.
    /// Все возвращаемые значения Trim()ятся, null заменяется на string.Empty.
    /// </summary>
    public class Row
    {
        private readonly string[] _values;
        private readonly Dictionary<string, int> _columnIndex;

        internal Row(string[] values, Dictionary<string, int> columnIndex)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _columnIndex = columnIndex ?? throw new ArgumentNullException(nameof(columnIndex));
        }

        /// <summary>
        /// Доступ к полю по имени столбца (без учёта регистра).
        /// Если столбец не найден — возвращает string.Empty.
        /// </summary>
        public string this[string columnName]
        {
            get
            {
                if (_columnIndex.TryGetValue(columnName, out int index))
                    return _values[index]?.Trim() ?? string.Empty;
                return string.Empty;
            }
        }

        /// <summary>
        /// Доступ к полю по индексу.
        /// Если индекс вне диапазона — возвращает string.Empty.
        /// </summary>
        public string this[int index]
        {
            get
            {
                if (index >= 0 && index < _values.Length)
                    return _values[index]?.Trim() ?? string.Empty;
                return string.Empty;
            }
            set
            {
                if (index >= 0 && index < _values.Length)
                    _values[index] = value;
            }
        }

        /// <summary>Количество полей в строке</summary>
        public int FieldCount => _values.Length;

        /// <summary>Имена всех столбцов</summary>
        public IEnumerable<string> ColumnNames => _columnIndex.Keys;

        /// <summary>Значения всех полей (с Trim)</summary>
        public IEnumerable<string> Values
        {
            get
            {
                for (int i = 0; i < _values.Length; i++)
                    yield return _values[i]?.Trim() ?? string.Empty;
            }
        }

        /// <summary>
        /// Пытается получить значение поля по имени столбца.
        /// </summary>
        /// <param name="columnName">Имя столбца (без учёта регистра)</param>
        /// <param name="value">Найденное значение или string.Empty</param>
        /// <returns>true, если столбец найден; иначе false</returns>
        public bool TryGet(string columnName, out string value)
        {
            if (_columnIndex.TryGetValue(columnName, out int index))
            {
                value = _values[index]?.Trim() ?? string.Empty;
                return true;
            }

            value = string.Empty;
            return false;
        }

        /// <summary>Проверяет наличие столбца в таблице</summary>
        public bool ContainsColumn(string columnName)
        {
            return _columnIndex.ContainsKey(columnName);
        }

        /// <summary>
        /// Проверяет, что поле по имени столбца существует и не пустое
        /// (после Trim, не null и не whitespace).
        /// </summary>
        public bool HasValue(string columnName)
        {
            if (!_columnIndex.TryGetValue(columnName, out int index))
                return false;

            return !string.IsNullOrWhiteSpace(_values[index]);
        }

        /// <summary>
        /// Проверяет, совпадает ли поле с указанным значением (без учёта регистра).
        /// Если столбец не найден — возвращает false.
        /// </summary>
        public bool EqualsValue(string columnName, string value)
        {
            return string.Equals(this[columnName], value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Проверяет, содержит ли значение в указанном столбце подстроку
        /// с учетом регистра.
        /// Если столбец не найден — возвращает false.
        /// </summary>
        public bool ContainsValue(string columnName, string part)
        {
            if (string.IsNullOrEmpty(columnName) || string.IsNullOrEmpty(part))
                return false;

            if (!_columnIndex.TryGetValue(columnName, out int index))
                return false;

            string cellValue = _values[index] ?? string.Empty;
            return cellValue.IndexOf(part, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Проверяет, содержит ли значение в указанном столбце <b>целое слово</b>
        /// с помощью regex-границы слова (<c>\b</c>).
        /// Поиск с учётом регистра (как и <see cref="ContainsValue"/>).
        /// Если столбец не найден или значение пустое — возвращает false.
        /// </summary>
        public bool ContainsWord(string columnName, string word)
        {
            if (string.IsNullOrEmpty(columnName) || string.IsNullOrEmpty(word))
                return false;

            if (!_columnIndex.TryGetValue(columnName, out int index))
                return false;

            string cellValue = _values[index];
            if (string.IsNullOrEmpty(cellValue))
                return false;

            return Regex.IsMatch(cellValue, $"\\b{Regex.Escape(word)}\\b");
        }
    }
}