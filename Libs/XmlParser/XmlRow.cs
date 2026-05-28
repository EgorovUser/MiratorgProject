using System.Collections.Generic;

namespace XmlParser
{
    /// <summary>
    /// Строка из XML-таблицы SAP. Хранит значения по именам столбцов
    /// (DocNumber, DocDate, EffectiveAmount, DocType и т.д.).
    /// </summary>
    public class XmlRow
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public int RowIndex { get; }

        public XmlRow(int rowIndex, Dictionary<string, string> values)
        {
            RowIndex = rowIndex;
            _values = values ?? throw new System.ArgumentNullException(nameof(values));
        }

        /// <summary>
        /// Получает значение по имени столбца. Возвращает null, если столбец не найден.
        /// </summary>
        public string GetValue(string columnName)
        {
            return _values.TryGetValue(columnName, out var value) ? value : null;
        }

        /// <summary>
        /// Пытается получить значение по имени столбца.
        /// </summary>
        public bool TryGetValue(string columnName, out string value)
        {
            return _values.TryGetValue(columnName, out value);
        }

        /// <summary>
        /// Получает значение по имени столбца через индексатор.
        /// </summary>
        public string this[string columnName] => GetValue(columnName);


    }
}
